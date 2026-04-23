using MySql.Data.MySqlClient;
using System.Globalization;
using Trackii.Models.Gerencia;

namespace Trackii.Services;

public class GerenciaService
{
    private readonly string _conn;
    private readonly IConfiguration _cfg;
    // El corte ahora es el tiempo real, siempre actualizado.
    private static DateTime CurrentCutoffUtc => DateTime.UtcNow;

    public GerenciaService(IConfiguration cfg)
    {
        _cfg = cfg;
        _conn = cfg.GetConnectionString("TrackiiDb")
            ?? throw new Exception("Connection string TrackiiDb no configurada");
    }

    public GerenciaDashboardVm GetDashboard()
    {
        var vm = new GerenciaDashboardVm();

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        var production = LoadProductionByLocation(cn);
        vm.ProductionByLocation.AddRange(production);
        vm.TopLocations.AddRange(production.OrderByDescending(item => item.QtyProduced).Take(5));
        vm.BottomLocations.AddRange(production.OrderBy(item => item.QtyProduced).Take(5));

        foreach (var item in production)
        {
            vm.ProductionByLocationChart.Labels.Add(item.Location);
            vm.ProductionByLocationChart.Values.Add(item.QtyProduced);
        }

        LoadWipStatusChart(cn, vm.WipStatusChart);
        LoadScanEventChart(cn, vm.ScanEventChart);
        LoadWorkOrderStatusChart(cn, vm.OrderStatusChart);
        LoadDashboardExecutiveSummary(cn, vm);
        LoadLocationBacklogChart(cn, vm);

        var weekStart = GetStartOfWeek(DateTime.UtcNow.Date);
        vm.WeeklyOutput = GetWeeklyOutputMatrix(cn, weekStart, weekStart.AddDays(6), "fifo");
        vm.DelayedOrders.AddRange(LoadDelayedWorkOrders(cn).Take(6));
        vm.NewOrders.AddRange(LoadNewestWorkOrders(cn, 6));

        return vm;
    }

    public GerenciaWeeklyOutputVm GetWeeklyOutput(string? periodType, string? weekValue, string? monthValue, DateTime? fromDate, DateTime? toDate)
    {
        var vm = new GerenciaWeeklyOutputVm
        {
            PeriodType = string.IsNullOrWhiteSpace(periodType) ? "week" : periodType,
            WeekValue = weekValue,
            MonthValue = monthValue,
            FromDate = fromDate,
            ToDate = toDate
        };

        ResolvePeriod(vm.PeriodType, vm.WeekValue, vm.MonthValue, vm.FromDate, vm.ToDate, out var start, out var end, out var normalizedPeriodType, out var normalizedWeek, out var normalizedMonth);

        vm.PeriodType = normalizedPeriodType;
        vm.WeekValue = normalizedWeek;
        vm.MonthValue = normalizedMonth;

        using var cn = new MySqlConnection(_conn);
        cn.Open();
        vm.Matrix = GetWeeklyOutputMatrix(cn, start, end, "fifo");

        return vm;
    }

    public GerenciaDiscreteMapVm GetDiscreteMap(string? periodType, string? weekValue, string? monthValue, DateTime? fromDate, DateTime? toDate, string? sortBy, string? metricView, string? selectedSubfamily)
    {
        var normalizedQuickRange = NormalizeQuickRange(periodType);
        var vm = new GerenciaDiscreteMapVm
        {
            PeriodType = normalizedQuickRange,
            WeekValue = weekValue,
            MonthValue = monthValue,
            FromDate = fromDate,
            ToDate = toDate,
            MetricView = NormalizeMetricView(metricView),
            SelectedSubfamily = selectedSubfamily,
            SortBy = string.IsNullOrWhiteSpace(sortBy) ? "fifo" : sortBy,
            QuickRange = normalizedQuickRange
        };

        ResolvePeriod(vm.PeriodType, vm.WeekValue, vm.MonthValue, vm.FromDate, vm.ToDate, out var start, out var end, out var normalizedPeriodType, out var normalizedWeek, out var normalizedMonth);

        vm.PeriodType = normalizedPeriodType;
        vm.WeekValue = normalizedWeek;
        vm.MonthValue = normalizedMonth;
        vm.SnapshotDate = end;

        using var cn = new MySqlConnection(_conn);
        cn.Open();
        vm.Matrix = GetDiscreteInventoryMatrix(cn, start, end, vm);
        vm.SelectedSubfamily = ResolveSelectedSubfamily(vm.Matrix.Subfamilies, vm.SelectedSubfamily);
        LoadDiscreteOrdersSummary(cn, vm, start, end);
        LoadDiscreteProductionVsScrap(cn, vm, start, end);

        return vm;
    }

    public GerenciaBackendLobbyVm GetBackendLobby(string? mode)
    {
        var vm = new GerenciaBackendLobbyVm
        {
            SnapshotAtUtc = CurrentCutoffUtc,
            DataCutoffUtc = CurrentCutoffUtc,
            ViewMode = GerenciaBackendLobbyVm.FullInventoryMode,
            OrdersOpenedFromDate = null
        };

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        var families = new List<(int FamilyId, string FamilyName, bool HasOpb)>();
        using (var cmd = new MySqlCommand(@"
            SELECT f.id AS family_id,
                   COALESCE(f.name, 'Sin familia') AS family_name,
                   MAX(CASE WHEN UPPER(COALESCE(s.name, '')) LIKE '%OPB%' THEN 1 ELSE 0 END) AS has_opb
            FROM family f
            LEFT JOIN subfamily s ON s.id_family = f.id AND s.active = 1
            WHERE f.active = 1
            GROUP BY f.id, f.name
            ORDER BY f.id", cn))
        {
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var familyName = rd.GetString("family_name").Trim();
                families.Add((
                    rd.GetInt32("family_id"),
                    string.IsNullOrWhiteSpace(familyName) ? "Sin familia" : familyName,
                    Convert.ToInt32(rd.GetValue(rd.GetOrdinal("has_opb"))) == 1
                ));
            }
        }

        foreach (var family in families)
        {
            vm.Columns.Add(family.FamilyName);
            if (family.HasOpb)
            {
                vm.Columns.Add(GetOpbColumnName(family.FamilyName));
            }
        }

        foreach (var goal in GetLobbyDailyGoals())
        {
            vm.DailyGoalsByColumn[goal.Key] = goal.Value;
        }

        foreach (var col in vm.Columns)
        {
            if (!vm.DailyGoalsByColumn.ContainsKey(col))
            {
                vm.DailyGoalsByColumn[col] = null;
            }
        }
        var rowByLocation = new Dictionary<string, BackendLobbyLocationRowVm>(StringComparer.OrdinalIgnoreCase);

        var familyById = families.ToDictionary(f => f.FamilyId, f => f.FamilyName);
        var opbFamilyIds = families.Where(f => f.HasOpb).Select(f => f.FamilyId).ToHashSet();

        using (var cmd = new MySqlCommand(@"
            SELECT l.id AS location_id,
                       CASE
                           WHEN COALESCE(l.name, '') LIKE '%Alloy%' THEN 'Alloy'
                           WHEN l.id = 8 OR COALESCE(l.name, '') LIKE '%Backfill%' THEN 'Backfill'
                           WHEN COALESCE(l.name, '') LIKE '%Molde%' THEN 'Moldeo'
                           WHEN COALESCE(l.name, '') LIKE '%Fast%' THEN 'FAST CAST'
                           WHEN COALESCE(l.name, '') LIKE '%Inspec%' THEN 'Inspeccion Final'
                           WHEN COALESCE(l.name, '') LIKE '%Tie%' THEN 'Tie Bar'
                           WHEN COALESCE(l.name, '') LIKE '%Tin%' THEN 'Tin Plate'
                           WHEN COALESCE(l.name, '') LIKE '%Emp%' THEN 'Empaque'
                           WHEN COALESCE(l.name, '') LIKE '%QC%' OR COALESCE(l.name, '') LIKE '%Q.C.%' OR COALESCE(l.name, '') LIKE '%Calidad%' THEN 'QC'
                           WHEN COALESCE(l.name, '') LIKE '%Prueba%' THEN 'Prueba Electrica'
                           ELSE COALESCE(l.name, 'Sin localidad')
                       END AS location_name,
                   f.id AS family_id,
                   UPPER(COALESCE(sf.name, '')) LIKE '%OPB%' AS is_opb,
                   COALESCE(SUM(COALESCE(last_qty.qty_in, 0)), 0) AS qty
            FROM wip_item wip
            JOIN work_order wo ON wo.id = wip.wo_order_id
            JOIN product p ON p.id = wo.product_id
            JOIN subfamily sf ON sf.id = p.id_subfamily
            JOIN family f ON f.id = sf.id_family
            LEFT JOIN route_step rs ON rs.id = wip.current_step_id
            LEFT JOIN location l ON l.id = rs.location_id
            LEFT JOIN (
                SELECT wse.wip_item_id,
                       wse.qty_in,
                       wse.create_at AS last_activity_date
                FROM wip_step_execution wse
                INNER JOIN (
                    SELECT wip_item_id, MAX(id) AS last_step_id
                    FROM wip_step_execution
                    GROUP BY wip_item_id
                ) latest ON latest.last_step_id = wse.id
            ) last_qty ON last_qty.wip_item_id = wip.id
            WHERE wo.active = 1
              AND wip.status = 'ACTIVE'
            GROUP BY location_id, location_name, f.id, is_opb", cn))
        {
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var locId = rd.IsDBNull(rd.GetOrdinal("location_id")) ? 0 : rd.GetInt32("location_id");
                var locationName = rd.GetString("location_name").Trim();

                if (!rowByLocation.TryGetValue(locationName, out var row))
                {
                    row = new BackendLobbyLocationRowVm { LocationName = locationName };
                    foreach (var colName in vm.Columns)
                    {
                        row.PiecesByColumn[colName] = 0;
                    }
                    rowByLocation[locationName] = row;
                }

                var familyId = rd.GetInt32("family_id");
                if (!familyById.TryGetValue(familyId, out var familyName))
                    continue;

                var isOpb = rd.GetBoolean("is_opb");
                var finalColumnName = isOpb && opbFamilyIds.Contains(familyId)
                    ? GetOpbColumnName(familyName)
                    : familyName;

                if (!row.PiecesByColumn.ContainsKey(finalColumnName))
                {
                    row.PiecesByColumn[finalColumnName] = 0;
                }

                row.PiecesByColumn[finalColumnName] += Convert.ToInt32(rd.GetValue(rd.GetOrdinal("qty")));
            }
        }

        vm.Rows.AddRange(rowByLocation.Values
            .OrderBy(row => GetBackendLobbyLocationOrder(row.LocationName))
            .ThenBy(row => row.LocationName, StringComparer.OrdinalIgnoreCase));

        vm.Groups.AddRange(
            vm.Rows.SelectMany(row => row.PiecesByColumn.Select(cell => new BackendLobbyGroupRowVm
            {
                LugarNombre = row.LocationName,
                LocationName = row.LocationName,
                FamilyGroupName = cell.Key,
                Piezas = cell.Value,
                Ordenes = 0
            })));
        return vm;
    }

    public GerenciaBackendLobbyVm GetRealInventoryLobby()
    {
        var vm = new GerenciaBackendLobbyVm
        {
            SnapshotAtUtc = CurrentCutoffUtc,
            DataCutoffUtc = CurrentCutoffUtc,
            ViewMode = GerenciaBackendLobbyVm.FullInventoryMode,
            OrdersOpenedFromDate = null
        };

        string[] orderedColumns =
        {
            "LATERAL LED",
            "LATERAL SENSOR",
            "LATERAL OPB",
            "MINI AXIAL",
            "MINI AXIAL OPB",
            "MAXI AXIAL",
            "FOTOLOGICO",
            "PHOTO OPBS"
        };

        var orderedLocations = new[]
        {
            "Alloy",
            "Backfill",
            "Moldeo",
            "FAST CAST",
            "Inspeccion Final",
            "Tie bar",
            "Tin plate",
            "Prueba Electrica",
            "Empaque",
            "QC",
            "Almacen"
        };

        vm.Columns.AddRange(orderedColumns);

        foreach (var location in orderedLocations)
        {
            var row = new BackendLobbyLocationRowVm { LocationName = location };
            foreach (var column in orderedColumns)
            {
                row.PiecesByColumn[column] = 0;
            }
            vm.Rows.Add(row);
        }

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(@"
            SELECT
                CASE
                    WHEN COALESCE(l.name, '') LIKE '%Alloy%' THEN 'Alloy'
                    WHEN l.id = 8 OR COALESCE(l.name, '') LIKE '%Backfill%' THEN 'Backfill'
                    WHEN COALESCE(l.name, '') LIKE '%Molde%' THEN 'Moldeo'
                    WHEN COALESCE(l.name, '') LIKE '%Fast%' THEN 'FAST CAST'
                    WHEN COALESCE(l.name, '') LIKE '%Inspec%' THEN 'Inspeccion Final'
                    WHEN COALESCE(l.name, '') LIKE '%Tie%' THEN 'Tie bar'
                    WHEN COALESCE(l.name, '') LIKE '%Tin%' THEN 'Tin plate'
                    WHEN COALESCE(l.name, '') LIKE '%Prueba%' THEN 'Prueba Electrica'
                    WHEN COALESCE(l.name, '') LIKE '%Emp%' THEN 'Empaque'
                    WHEN COALESCE(l.name, '') LIKE '%QC%' OR COALESCE(l.name, '') LIKE '%Q.C.%' OR COALESCE(l.name, '') LIKE '%Calidad%' THEN 'QC'
                    WHEN COALESCE(l.name, '') LIKE '%Almacen%' OR COALESCE(l.name, '') LIKE '%Almacén%' THEN 'Almacen'
                    ELSE NULL
                END AS normalized_location,
                CASE
                    WHEN UPPER(f.name) = 'LATERAL LED' THEN 'LATERAL LED'
                    WHEN UPPER(f.name) = 'LATERAL SENSOR' AND UPPER(COALESCE(sf.name, '')) NOT LIKE '%OPB%' THEN 'LATERAL SENSOR'
                    WHEN UPPER(f.name) = 'LATERAL SENSOR' AND UPPER(COALESCE(sf.name, '')) LIKE '%OPB%' THEN 'LATERAL OPB'
                    WHEN UPPER(f.name) = 'MINI AXIALES' AND UPPER(COALESCE(sf.name, '')) NOT LIKE '%OPB%' THEN 'MINI AXIAL'
                    WHEN UPPER(f.name) = 'MINI AXIALES' AND UPPER(COALESCE(sf.name, '')) LIKE '%OPB%' THEN 'MINI AXIAL OPB'
                    WHEN UPPER(f.name) = 'MAXI AXIALES' THEN 'MAXI AXIAL'
                    WHEN UPPER(f.name) = 'FOTOLOGICOS' AND UPPER(COALESCE(sf.name, '')) NOT LIKE '%OPB%' THEN 'FOTOLOGICO'
                    WHEN UPPER(f.name) = 'FOTOLOGICOS' AND UPPER(COALESCE(sf.name, '')) LIKE '%OPB%' THEN 'PHOTO OPBS'
                    ELSE NULL
                END AS inventory_column,
                COALESCE(SUM(COALESCE(last_qty.qty_in, 0)), 0) AS qty_in
            FROM wip_item wip
            JOIN work_order wo ON wo.id = wip.wo_order_id
            JOIN product p ON p.id = wo.product_id
            JOIN subfamily sf ON sf.id = p.id_subfamily
            JOIN family f ON f.id = sf.id_family
            LEFT JOIN route_step rs ON rs.id = wip.current_step_id
            LEFT JOIN location l ON l.id = rs.location_id
            LEFT JOIN (
                SELECT wse.wip_item_id,
                       wse.qty_in
                FROM wip_step_execution wse
                INNER JOIN (
                    SELECT wip_item_id, MAX(id) AS last_step_id
                    FROM wip_step_execution
                    GROUP BY wip_item_id
                ) latest ON latest.last_step_id = wse.id
            ) last_qty ON last_qty.wip_item_id = wip.id
            WHERE wo.active = 1
              AND wip.status = 'ACTIVE'
            GROUP BY normalized_location, inventory_column", cn);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            if (rd.IsDBNull(rd.GetOrdinal("normalized_location")) || rd.IsDBNull(rd.GetOrdinal("inventory_column")))
                continue;

            var location = rd.GetString("normalized_location").Trim();
            var column = rd.GetString("inventory_column").Trim();
            var qty = rd.GetInt32("qty_in");

            var row = vm.Rows.FirstOrDefault(r => r.LocationName.Equals(location, StringComparison.OrdinalIgnoreCase));
            if (row is null)
                continue;

            if (!vm.Columns.Contains(column, StringComparer.Ordinal))
                continue;

            if (!row.PiecesByColumn.ContainsKey(column))
                row.PiecesByColumn[column] = 0;

            row.PiecesByColumn[column] += qty;
        }

        vm.Groups.AddRange(
            vm.Rows.SelectMany(row => row.PiecesByColumn.Select(cell => new BackendLobbyGroupRowVm
            {
                LugarNombre = row.LocationName,
                LocationName = row.LocationName,
                FamilyGroupName = cell.Key,
                Piezas = cell.Value,
                Ordenes = 0
            })));

        return vm;
    }

    public GerenciaLobbyInventoryCellDetailVm GetBackendLobbyCellDetail(string location, string familyGroup, string? mode)
    {
        var vm = new GerenciaLobbyInventoryCellDetailVm
        {
            Location = location.Trim(),
            FamilyGroup = familyGroup.Trim(),
            ViewMode = GerenciaBackendLobbyVm.FullInventoryMode,
            OrdersOpenedFromDate = null
        };

        var normalizedFamilyGroup = vm.FamilyGroup.Trim().ToUpperInvariant();
        var isOpbColumn =
            normalizedFamilyGroup.StartsWith("OPB ", StringComparison.OrdinalIgnoreCase) ||
            normalizedFamilyGroup.EndsWith(" OPB", StringComparison.OrdinalIgnoreCase) ||
            normalizedFamilyGroup.Contains("PHOTO OPBS", StringComparison.OrdinalIgnoreCase);
        var baseFamily = NormalizeFamilyAlias(vm.FamilyGroup);

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(@"
            SELECT wo.wo_number,
                   wo.status,
                   p.part_number,
                   COALESCE(sf.name, 'Sin subfamilia') AS subfamily_name,
                   wip.id AS wip_id,
                   wip.status AS wip_status,
                   wip.created_at,
                   CASE
                       WHEN COALESCE(l.name, '') LIKE '%Alloy%' THEN 'Alloy'
                       WHEN l.id = 8 OR COALESCE(l.name, '') LIKE '%Backfill%' THEN 'Backfill'
                       WHEN COALESCE(l.name, '') LIKE '%Molde%' THEN 'Moldeo'
                       WHEN COALESCE(l.name, '') LIKE '%Fast%' THEN 'FAST CAST'
                       WHEN COALESCE(l.name, '') LIKE '%Inspec%' THEN 'Inspeccion Final'
                       WHEN COALESCE(l.name, '') LIKE '%Tie%' THEN 'Tie Bar'
                       WHEN COALESCE(l.name, '') LIKE '%Tin%' THEN 'Tin Plate'
                       WHEN COALESCE(l.name, '') LIKE '%Emp%' THEN 'Empaque'
                       WHEN COALESCE(l.name, '') LIKE '%QC%' OR COALESCE(l.name, '') LIKE '%Q.C.%' OR COALESCE(l.name, '') LIKE '%Calidad%' THEN 'QC'
                       WHEN COALESCE(l.name, '') LIKE '%Prueba%' THEN 'Prueba Electrica'
                       ELSE COALESCE(l.name, 'Sin localidad')
                   END AS normalized_location,
                   COALESCE(last_qty.qty_in, 0) AS current_qty
            FROM wip_item wip
            JOIN work_order wo ON wo.id = wip.wo_order_id
            JOIN product p ON p.id = wo.product_id
            JOIN subfamily sf ON sf.id = p.id_subfamily
            JOIN family f ON f.id = sf.id_family
            LEFT JOIN route_step rs ON rs.id = wip.current_step_id
            LEFT JOIN location l ON l.id = rs.location_id
            LEFT JOIN (
                SELECT wse.wip_item_id,
                       wse.qty_in,
                       wse.create_at AS last_activity_date
                FROM wip_step_execution wse
                INNER JOIN (
                    SELECT wip_item_id, MAX(id) AS last_step_id
                    FROM wip_step_execution
                    GROUP BY wip_item_id
                ) latest ON latest.last_step_id = wse.id
            ) last_qty ON last_qty.wip_item_id = wip.id
            WHERE wo.active = 1
              AND wip.status = 'ACTIVE'
              AND UPPER(COALESCE(f.name, 'Sin familia')) = UPPER(@baseFamily)
              AND (
                    @isOpbColumn = 0 AND UPPER(COALESCE(sf.name, '')) NOT LIKE '%OPB%'
                    OR @isOpbColumn = 1 AND UPPER(COALESCE(sf.name, '')) LIKE '%OPB%'
                  )
            ORDER BY COALESCE(last_qty.last_activity_date, wip.created_at) DESC, wo.wo_number", cn);

        cmd.Parameters.AddWithValue("@baseFamily", baseFamily);
        cmd.Parameters.AddWithValue("@isOpbColumn", isOpbColumn ? 1 : 0);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var wipIdOrdinal = rd.GetOrdinal("wip_id");
            var wipStatusOrdinal = rd.GetOrdinal("wip_status");
            var createdAtOrdinal = rd.GetOrdinal("created_at");

            vm.Orders.Add(new WorkOrderVm
            {
                WoNumber = rd.GetString("wo_number"),
                Status = rd.GetString("status"),
                Product = rd.GetString("part_number"),
                Subfamily = rd.GetString("subfamily_name"),
                Qty = Convert.ToInt32(rd.GetInt64("current_qty")),
                WipItemId = rd.IsDBNull(wipIdOrdinal) ? null : rd.GetUInt32(wipIdOrdinal),
                WipStatus = rd.IsDBNull(wipStatusOrdinal) ? null : rd.GetString(wipStatusOrdinal),
                CurrentLocation = rd.GetString("normalized_location"),
                WipCreatedAt = rd.IsDBNull(createdAtOrdinal) ? null : rd.GetDateTime(createdAtOrdinal)
            });
        }

        return vm;
    }

    private static string NormalizeFamilyAlias(string baseFamily)
    {
        var normalized = baseFamily.Trim().ToUpperInvariant();
        if (normalized is "LATERAL OPB" or "LATERAL SENSOR")
            return "LATERAL SENSOR";
        if (normalized is "LATERAL" or "LATERAL SENSOR SIN OPB")
            return "LATERAL SENSOR";
        if (normalized is "MINI AXIAL OPB")
            return "MINI AXIALES";
        if (normalized is "MINI AXIAL" or "MINI AXIALES" or "MINIAXIAL")
            return "MINI AXIALES";
        if (normalized is "MAXI AXIAL" or "MAXI AXIALES")
            return "MAXI AXIALES";
        if (normalized is "PHOTO OPBS")
            return "FOTOLOGICOS";
        if (normalized is "FOTOLOGICO" or "FOTOLOGICOS")
            return "FOTOLOGICOS";
        return baseFamily.Trim();
    }

    private static int GetBackendLobbyLocationOrder(string locationName)
    {
        return locationName.Trim().ToUpperInvariant() switch
        {
            "ALLOY" => 1,
            "BACKFILL" => 2,
            "MOLDEO" => 3,
            "MODELO" => 3,
            "FAST CAST" => 4,
            "INSPECCION FINAL" => 5,
            "TIE BAR" => 6,
            "TIN PLATE" => 7,
            "PRUEBA ELECTRICA" => 8,
            "EMPAQUE" => 9,
            "QC" => 10,
            _ => 999
        };
    }

    private Dictionary<string, int> GetLobbyDailyGoals()
    {
        var defaults = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["LATERAL LED"] = 93000,
            ["LATERAL SENSOR"] = 36500,
            ["OPB LATERAL"] = 11500,
            ["MINI AXIALES"] = 16500,
            ["OPB MINI AXIAL"] = 3600,
            ["MAXI AXIALES"] = 8500,
            ["FOTOLOGICOS"] = 38000,
            ["OPB FOTO"] = 3600
        };

        var fromConfig = _cfg.GetSection("Gerencia:LobbyDailyGoals").GetChildren();
        foreach (var item in fromConfig)
        {
            if (!int.TryParse(item.Value, out var parsed))
                continue;

            var key = (item.Key ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key))
                continue;

            defaults[key] = parsed;
        }

        return defaults;
    }

    private static string GetOpbColumnName(string familyName)
    {
        var normalized = familyName.ToUpperInvariant();
        if (normalized.Contains("LATERAL"))
            return "OPB LATERAL";
        if (normalized.Contains("MINI"))
            return "OPB MINI AXIAL";
        if (normalized.Contains("FOTO"))
            return "OPB FOTO";
        return $"OPB {familyName}";
    }

    public GerenciaDayDetailVm GetDiscreteDayDetail(DateTime day, string? sortBy)
    {
        var vm = new GerenciaDayDetailVm
        {
            Day = day.Date,
            SortBy = string.IsNullOrWhiteSpace(sortBy) ? "fifo" : sortBy
        };

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(@"
            SELECT wo.wo_number,
                   p.part_number,
                   COALESCE(sf.name, 'Sin subfamilia') AS subfamily_name,
                   l.name AS location_name,
                   MIN(wip.created_at) AS wip_start_at,
                   COALESCE(SUM(wse.qty_in), 0) AS qty_produced,
                   COALESCE(SUM(wse.qty_scrap), 0) AS qty_scrap
            FROM wip_step_execution wse
            JOIN wip_item wip ON wip.id = wse.wip_item_id
            JOIN work_order wo ON wo.id = wip.wo_order_id
            JOIN product p ON p.id = wo.product_id
            LEFT JOIN subfamily sf ON sf.id = p.id_subfamily
            LEFT JOIN location l ON l.id = wse.location_id
            WHERE DATE(wse.create_at) = @day
            GROUP BY wo.wo_number, p.part_number, subfamily_name, location_name
            ORDER BY " + BuildSortSql(vm.SortBy), cn);

        cmd.Parameters.AddWithValue("@day", vm.Day);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var woOrdinal = rd.GetOrdinal("wo_number");
            var productOrdinal = rd.GetOrdinal("part_number");
            var subfamilyOrdinal = rd.GetOrdinal("subfamily_name");
            var locationOrdinal = rd.GetOrdinal("location_name");
            var startOrdinal = rd.GetOrdinal("wip_start_at");
            var qtyOrdinal = rd.GetOrdinal("qty_produced");
            var scrapOrdinal = rd.GetOrdinal("qty_scrap");

            vm.Orders.Add(new DailyOrderDetailVm
            {
                WoNumber = rd.IsDBNull(woOrdinal) ? "Sin orden" : rd.GetString(woOrdinal),
                Product = rd.IsDBNull(productOrdinal) ? "Sin producto" : rd.GetString(productOrdinal),
                Subfamily = rd.IsDBNull(subfamilyOrdinal) ? "Sin subfamilia" : rd.GetString(subfamilyOrdinal),
                Location = rd.IsDBNull(locationOrdinal) ? null : rd.GetString(locationOrdinal),
                WipStartAt = rd.IsDBNull(startOrdinal) ? null : rd.GetDateTime(startOrdinal),
                Qty = rd.IsDBNull(qtyOrdinal) ? 0 : Convert.ToInt32(rd.GetValue(qtyOrdinal)),
                Scrap = rd.IsDBNull(scrapOrdinal) ? 0 : Convert.ToInt32(rd.GetValue(scrapOrdinal))
            });
        }

        return vm;
    }

    public GerenciaDiscreteCellDetailVm GetDiscreteMapCellDetail(
        string location,
        string subfamily,
        string? periodType,
        string? weekValue,
        string? monthValue,
        DateTime? fromDate,
        DateTime? toDate,
        DateTime? day)
    {
        if (string.IsNullOrWhiteSpace(location)) throw new Exception("Localidad requerida.");
        if (string.IsNullOrWhiteSpace(subfamily)) throw new Exception("Subfamilia requerida.");

        ResolvePeriod(periodType, weekValue, monthValue, fromDate, toDate, out var start, out var end, out var normalizedPeriodType, out var normalizedWeek, out var normalizedMonth);

        var vm = new GerenciaDiscreteCellDetailVm
        {
            Location = location,
            Subfamily = subfamily,
            PeriodType = normalizedPeriodType,
            WeekValue = normalizedWeek,
            MonthValue = normalizedMonth,
            StartDate = start,
            EndDate = end,
            Day = day?.Date
        };

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var totalCmd = new MySqlCommand(@"
            SELECT COALESCE(SUM(wse.qty_in), 0) AS qty_total,
                   COALESCE(SUM(wse.qty_scrap), 0) AS scrap_total,
                   COUNT(DISTINCT wo.id) AS orders_total
            FROM wip_step_execution wse
            JOIN route_step rs ON rs.id = wse.route_step_id
            JOIN location l ON l.id = rs.location_id
            JOIN wip_item wip ON wip.id = wse.wip_item_id
            JOIN work_order wo ON wo.id = wip.wo_order_id
            JOIN product p ON p.id = wo.product_id
            LEFT JOIN subfamily sf ON sf.id = p.id_subfamily
            WHERE l.name = @location
              AND COALESCE(sf.name, 'Sin subfamilia') = @subfamily
              AND DATE(wse.create_at) BETWEEN @startDate AND @endDate
              AND (@day IS NULL OR DATE(wse.create_at) = @day)", cn);

        totalCmd.Parameters.AddWithValue("@location", vm.Location);
        totalCmd.Parameters.AddWithValue("@subfamily", vm.Subfamily);
        totalCmd.Parameters.AddWithValue("@startDate", vm.StartDate);
        totalCmd.Parameters.AddWithValue("@endDate", vm.EndDate);
        totalCmd.Parameters.AddWithValue("@day", vm.Day);

        using (var rd = totalCmd.ExecuteReader())
        {
            if (rd.Read())
            {
                vm.TotalQty = Convert.ToInt32(rd.GetInt64("qty_total"));
                vm.TotalScrap = Convert.ToInt32(rd.GetInt64("scrap_total"));
                vm.TotalOrders = Convert.ToInt32(rd.GetInt64("orders_total"));
            }
        }

        using var dailyCmd = new MySqlCommand(@"
            SELECT DATE(wse.create_at) AS day,
                   COALESCE(SUM(wse.qty_in), 0) AS qty_total,
                   COALESCE(SUM(wse.qty_scrap), 0) AS scrap_total,
                   COUNT(DISTINCT wo.id) AS orders_total
            FROM wip_step_execution wse
            JOIN route_step rs ON rs.id = wse.route_step_id
            JOIN location l ON l.id = rs.location_id
            JOIN wip_item wip ON wip.id = wse.wip_item_id
            JOIN work_order wo ON wo.id = wip.wo_order_id
            JOIN product p ON p.id = wo.product_id
            LEFT JOIN subfamily sf ON sf.id = p.id_subfamily
            WHERE l.name = @location
              AND COALESCE(sf.name, 'Sin subfamilia') = @subfamily
              AND DATE(wse.create_at) BETWEEN @startDate AND @endDate
              AND (@day IS NULL OR DATE(wse.create_at) = @day)
            GROUP BY DATE(wse.create_at)
            ORDER BY DATE(wse.create_at)", cn);

        dailyCmd.Parameters.AddWithValue("@location", vm.Location);
        dailyCmd.Parameters.AddWithValue("@subfamily", vm.Subfamily);
        dailyCmd.Parameters.AddWithValue("@startDate", vm.StartDate);
        dailyCmd.Parameters.AddWithValue("@endDate", vm.EndDate);
        dailyCmd.Parameters.AddWithValue("@day", vm.Day);

        using (var rd = dailyCmd.ExecuteReader())
        {
            while (rd.Read())
            {
                vm.DailyRows.Add(new DiscreteCellDailyRowVm
                {
                    Day = rd.GetDateTime("day"),
                    Qty = Convert.ToInt32(rd.GetInt64("qty_total")),
                    Scrap = Convert.ToInt32(rd.GetInt64("scrap_total")),
                    Orders = Convert.ToInt32(rd.GetInt64("orders_total"))
                });
            }
        }

        using var detailCmd = new MySqlCommand(@"
            SELECT DATE(wse.create_at) AS day,
                   wo.wo_number,
                   p.part_number,
                   COALESCE(SUM(wse.qty_in), 0) AS qty_total,
                   COALESCE(SUM(wse.qty_scrap), 0) AS scrap_total,
                   MIN(wse.create_at) AS first_capture_at,
                   MAX(wse.create_at) AS last_capture_at,
                   wo.status AS wo_status
            FROM wip_step_execution wse
            JOIN route_step rs ON rs.id = wse.route_step_id
            JOIN location l ON l.id = rs.location_id
            JOIN wip_item wip ON wip.id = wse.wip_item_id
            JOIN work_order wo ON wo.id = wip.wo_order_id
            JOIN product p ON p.id = wo.product_id
            LEFT JOIN subfamily sf ON sf.id = p.id_subfamily
            WHERE l.name = @location
              AND COALESCE(sf.name, 'Sin subfamilia') = @subfamily
              AND DATE(wse.create_at) BETWEEN @startDate AND @endDate
              AND (@day IS NULL OR DATE(wse.create_at) = @day)
            GROUP BY DATE(wse.create_at), wo.id, wo.wo_number, p.part_number, wo.status
            ORDER BY DATE(wse.create_at) DESC, wo.wo_number, p.part_number", cn);

        detailCmd.Parameters.AddWithValue("@location", vm.Location);
        detailCmd.Parameters.AddWithValue("@subfamily", vm.Subfamily);
        detailCmd.Parameters.AddWithValue("@startDate", vm.StartDate);
        detailCmd.Parameters.AddWithValue("@endDate", vm.EndDate);
        detailCmd.Parameters.AddWithValue("@day", vm.Day);

        using var detailReader = detailCmd.ExecuteReader();
        while (detailReader.Read())
        {
            vm.OrderRows.Add(new DiscreteCellOrderRowVm
            {
                Day = detailReader.GetDateTime("day"),
                WoNumber = detailReader.GetString("wo_number"),
                PartNumber = detailReader.GetString("part_number"),
                Qty = Convert.ToInt32(detailReader.GetInt64("qty_total")),
                Scrap = Convert.ToInt32(detailReader.GetInt64("scrap_total")),
                FirstCaptureAt = detailReader.GetDateTime("first_capture_at"),
                LastCaptureAt = detailReader.GetDateTime("last_capture_at"),
                WoStatus = detailReader.GetString("wo_status")
            });
        }

        return vm;
    }

    public GerenciaDiscreteDailyPanelsVm GetDiscreteDailyPanels()
    {
        var vm = new GerenciaDiscreteDailyPanelsVm
        {
            Day = DateTime.UtcNow.Date
        };

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(@"
            WITH daily_location AS (
                SELECT wse.location_id,
                       COUNT(DISTINCT wo.id) AS orders_count,
                       COALESCE(SUM(wse.qty_in), 0) AS qty_produced
                FROM wip_step_execution wse
                JOIN wip_item wip ON wip.id = wse.wip_item_id
                JOIN work_order wo ON wo.id = wip.wo_order_id
                WHERE DATE(wse.create_at) = @day
                GROUP BY wse.location_id
            )
            SELECT l.name AS location_name,
                   COALESCE(dl.orders_count, 0) AS orders_count,
                   COALESCE(dl.qty_produced, 0) AS qty_produced
            FROM location l
            LEFT JOIN daily_location dl ON dl.location_id = l.id
            ORDER BY l.name", cn);

        cmd.Parameters.AddWithValue("@day", vm.Day);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            vm.Locations.Add(new DailyLocationPanelVm
            {
                Location = rd.GetString("location_name"),
                OrdersCount = Convert.ToInt32(rd.GetInt64("orders_count")),
                PiecesTotal = Convert.ToInt32(rd.GetInt64("qty_produced"))
            });
        }

        return vm;
    }

    public GerenciaScrapCausesVm GetScrapCauses(DateTime? day, string? woNumber, string? product)
    {
        var vm = new GerenciaScrapCausesVm
        {
            Day = day?.Date,
            WoNumber = woNumber,
            Product = product
        };

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(@"
            SELECT CONCAT(ec.code, ' · ', ec.description) AS cause,
                   COALESCE(SUM(sl.qty), 0) AS qty,
                   COUNT(*) AS events
            FROM scrap_log sl
            JOIN wip_item wip ON wip.id = sl.wip_item_id
            JOIN work_order wo ON wo.id = wip.wo_order_id
            JOIN product p ON p.id = wo.product_id
            JOIN error_code ec ON ec.id = sl.error_code_id
            WHERE (@day IS NULL OR DATE(sl.created_at) = @day)
              AND (@wo IS NULL OR wo.wo_number = @wo)
              AND (@product IS NULL OR p.part_number = @product)
            GROUP BY cause
            ORDER BY qty DESC, events DESC", cn);

        cmd.Parameters.AddWithValue("@day", vm.Day);
        cmd.Parameters.AddWithValue("@wo", string.IsNullOrWhiteSpace(vm.WoNumber) ? null : vm.WoNumber);
        cmd.Parameters.AddWithValue("@product", string.IsNullOrWhiteSpace(vm.Product) ? null : vm.Product);

        {
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                vm.Causes.Add(new ScrapCauseVm
                {
                    Cause = rd.GetString("cause"),
                    Qty = Convert.ToInt32(rd.GetInt64("qty")),
                    Events = Convert.ToInt32(rd.GetInt64("events"))
                });
            }
        }

        vm.TotalQty = vm.Causes.Sum(item => item.Qty);
        vm.TotalEvents = vm.Causes.Sum(item => item.Events);

        using var scrapDetailCmd = new MySqlCommand(@"
            SELECT sl.created_at,
                   wo.wo_number,
                   p.part_number,
                   ec.code AS error_code,
                   ecat.name AS error_category,
                   ec.description AS error_description,
                   l.name AS location_name,
                   u.username,
                   sl.qty,
                   sl.comments
            FROM scrap_log sl
            JOIN wip_item wip ON wip.id = sl.wip_item_id
            JOIN work_order wo ON wo.id = wip.wo_order_id
            JOIN product p ON p.id = wo.product_id
            JOIN error_code ec ON ec.id = sl.error_code_id
            JOIN error_category ecat ON ecat.id = ec.category_id
            JOIN route_step rs ON rs.id = sl.route_step_id
            JOIN location l ON l.id = rs.location_id
            JOIN `user` u ON u.id = sl.user_id
            WHERE (@day IS NULL OR DATE(sl.created_at) = @day)
              AND (@wo IS NULL OR wo.wo_number = @wo)
              AND (@product IS NULL OR p.part_number = @product)
            ORDER BY sl.created_at DESC, sl.id DESC
            LIMIT 200", cn);

        scrapDetailCmd.Parameters.AddWithValue("@day", vm.Day);
        scrapDetailCmd.Parameters.AddWithValue("@wo", string.IsNullOrWhiteSpace(vm.WoNumber) ? null : vm.WoNumber);
        scrapDetailCmd.Parameters.AddWithValue("@product", string.IsNullOrWhiteSpace(vm.Product) ? null : vm.Product);

        using var scrapDetailReader = scrapDetailCmd.ExecuteReader();
        while (scrapDetailReader.Read())
        {
            var commentsOrdinal = scrapDetailReader.GetOrdinal("comments");

            vm.Entries.Add(new ScrapLogEntryVm
            {
                CreatedAt = scrapDetailReader.GetDateTime("created_at"),
                WoNumber = scrapDetailReader.GetString("wo_number"),
                Product = scrapDetailReader.GetString("part_number"),
                ErrorCode = scrapDetailReader.GetString("error_code"),
                ErrorCategory = scrapDetailReader.GetString("error_category"),
                ErrorDescription = scrapDetailReader.GetString("error_description"),
                Location = scrapDetailReader.GetString("location_name"),
                UserName = scrapDetailReader.GetString("username"),
                Qty = Convert.ToInt32(scrapDetailReader.GetInt64("qty")),
                Comments = scrapDetailReader.IsDBNull(commentsOrdinal) ? null : scrapDetailReader.GetString(commentsOrdinal)
            });
        }

        return vm;
    }

    public GerenciaActiveOrdersVm GetActiveOrdersDetail()
    {
        var vm = new GerenciaActiveOrdersVm();

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        vm.ActiveOrders.AddRange(LoadWorkOrdersByStatus(cn, "OPEN"));
        vm.InProgressOrders.AddRange(LoadWorkOrdersByStatus(cn, "IN_PROGRESS"));

        return vm;
    }

    public GerenciaErrorCausesVm GetErrorCauses()
    {
        var vm = new GerenciaErrorCausesVm();

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(@"
            SELECT CONCAT(ec.code, ' · ', ec.description, ' (', ecat.name, ')') AS cause,
                   COALESCE(SUM(sl.qty), 0) AS qty,
                   COUNT(*) AS events
            FROM scrap_log sl
            JOIN error_code ec ON ec.id = sl.error_code_id
            JOIN error_category ecat ON ecat.id = ec.category_id
            GROUP BY ec.id, ec.code, ec.description, ecat.name
            ORDER BY qty DESC, events DESC
            LIMIT 10", cn);

        {
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var cause = new ScrapCauseVm
                {
                    Cause = rd.GetString("cause"),
                    Qty = Convert.ToInt32(rd.GetInt64("qty")),
                    Events = Convert.ToInt32(rd.GetInt64("events"))
                };
                vm.Causes.Add(cause);
                vm.CausesChart.Labels.Add(cause.Cause);
                vm.CausesChart.Values.Add(cause.Qty);
            }
        }

        {
            using var cmdByLocation = new MySqlCommand(@"
                SELECT l.name AS location_name,
                       COALESCE(SUM(sl.qty), 0) AS qty,
                       COUNT(*) AS events
                FROM scrap_log sl
                JOIN route_step rs ON rs.id = sl.route_step_id
                JOIN location l ON l.id = rs.location_id
                GROUP BY l.id, l.name
                ORDER BY qty DESC, events DESC
                LIMIT 10", cn);

            using var rdByLocation = cmdByLocation.ExecuteReader();
            while (rdByLocation.Read())
            {
                var row = new ScrapLocationVm
                {
                    Location = rdByLocation.GetString("location_name"),
                    Qty = Convert.ToInt32(rdByLocation.GetInt64("qty")),
                    Events = Convert.ToInt32(rdByLocation.GetInt64("events"))
                };

                vm.Locations.Add(row);
                vm.LocationScrapChart.Labels.Add(row.Location);
                vm.LocationScrapChart.Values.Add(row.Qty);
            }
        }

        {
            using var cmdErrorFrequency = new MySqlCommand(@"
                SELECT CONCAT(ec.code, ' · ', ec.description) AS error_name,
                       COUNT(*) AS events
                FROM scrap_log sl
                JOIN error_code ec ON ec.id = sl.error_code_id
                GROUP BY ec.id, ec.code, ec.description
                ORDER BY events DESC
                LIMIT 8", cn);

            using var rdErrorFrequency = cmdErrorFrequency.ExecuteReader();
            while (rdErrorFrequency.Read())
            {
                vm.ErrorFrequencyChart.Labels.Add(rdErrorFrequency.GetString("error_name"));
                vm.ErrorFrequencyChart.Values.Add(Convert.ToInt32(rdErrorFrequency.GetInt64("events")));
            }
        }

        {
            using var cmdLocationErrorMatrix = new MySqlCommand(@"
                SELECT l.name AS location_name,
                       ec.code AS error_code,
                       COALESCE(SUM(sl.qty), 0) AS qty,
                       COUNT(*) AS events
                FROM scrap_log sl
                JOIN route_step rs ON rs.id = sl.route_step_id
                JOIN location l ON l.id = rs.location_id
                JOIN error_code ec ON ec.id = sl.error_code_id
                GROUP BY l.id, l.name, ec.id, ec.code
                ORDER BY qty DESC, events DESC
                LIMIT 40", cn);

            using var rdLocationErrorMatrix = cmdLocationErrorMatrix.ExecuteReader();
            while (rdLocationErrorMatrix.Read())
            {
                vm.LocationErrorMatrix.Add(new LocationErrorMatrixRowVm
                {
                    Location = rdLocationErrorMatrix.GetString("location_name"),
                    ErrorCode = rdLocationErrorMatrix.GetString("error_code"),
                    Qty = Convert.ToInt32(rdLocationErrorMatrix.GetInt64("qty")),
                    Events = Convert.ToInt32(rdLocationErrorMatrix.GetInt64("events"))
                });
            }
        }

        {
            using var cmdProductCause = new MySqlCommand(@"
                SELECT p.part_number,
                       CONCAT(ec.code, ' · ', ec.description) AS cause,
                       COALESCE(SUM(sl.qty), 0) AS qty,
                       COUNT(*) AS events
                FROM scrap_log sl
                JOIN wip_item wip ON wip.id = sl.wip_item_id
                JOIN work_order wo ON wo.id = wip.wo_order_id
                JOIN product p ON p.id = wo.product_id
                JOIN error_code ec ON ec.id = sl.error_code_id
                GROUP BY p.id, p.part_number, ec.id, ec.code, ec.description
                ORDER BY qty DESC, events DESC
                LIMIT 12", cn);

            using var rdProductCause = cmdProductCause.ExecuteReader();
            while (rdProductCause.Read())
            {
                vm.ProductCauseRows.Add(new ProductErrorCauseVm
                {
                    Product = rdProductCause.GetString("part_number"),
                    Cause = rdProductCause.GetString("cause"),
                    Qty = Convert.ToInt32(rdProductCause.GetInt64("qty")),
                    Events = Convert.ToInt32(rdProductCause.GetInt64("events"))
                });
            }
        }

        {
            using var cmdTopLosses = new MySqlCommand(@"
                SELECT wo.wo_number,
                       p.part_number,
                       COALESCE(MIN(l.name), 'Sin localidad') AS location_name,
                       COALESCE(SUM(sl.qty), 0) AS qty,
                       COUNT(*) AS events
                FROM scrap_log sl
                JOIN wip_item wip ON wip.id = sl.wip_item_id
                JOIN work_order wo ON wo.id = wip.wo_order_id
                JOIN product p ON p.id = wo.product_id
                LEFT JOIN route_step rs ON rs.id = sl.route_step_id
                LEFT JOIN location l ON l.id = rs.location_id
                GROUP BY wo.id, wo.wo_number, p.part_number
                ORDER BY qty DESC, events DESC
                LIMIT 10", cn);

            using var rdTopLosses = cmdTopLosses.ExecuteReader();
            while (rdTopLosses.Read())
            {
                vm.TopOrderLosses.Add(new OrderLossVm
                {
                    WoNumber = rdTopLosses.GetString("wo_number"),
                    Product = rdTopLosses.GetString("part_number"),
                    MainLocation = rdTopLosses.GetString("location_name"),
                    Qty = Convert.ToInt32(rdTopLosses.GetInt64("qty")),
                    Events = Convert.ToInt32(rdTopLosses.GetInt64("events"))
                });
            }
        }

        {
            using var cmdTopReporters = new MySqlCommand(@"
                SELECT u.username,
                       COALESCE(SUM(sl.qty), 0) AS qty,
                       COUNT(*) AS events,
                       MAX(sl.created_at) AS last_record
                FROM scrap_log sl
                JOIN `user` u ON u.id = sl.user_id
                GROUP BY u.id, u.username
                ORDER BY qty DESC, events DESC
                LIMIT 10", cn);

            using var rdTopReporters = cmdTopReporters.ExecuteReader();
            while (rdTopReporters.Read())
            {
                vm.TopReporters.Add(new UserScrapActivityVm
                {
                    UserName = rdTopReporters.GetString("username"),
                    Qty = Convert.ToInt32(rdTopReporters.GetInt64("qty")),
                    Events = Convert.ToInt32(rdTopReporters.GetInt64("events")),
                    LastRecord = rdTopReporters.GetDateTime("last_record")
                });
            }
        }

        {
            using var cmdByHour = new MySqlCommand(@"
                SELECT HOUR(sl.created_at) AS hour_mark,
                       COALESCE(SUM(sl.qty), 0) AS qty
                FROM scrap_log sl
                GROUP BY HOUR(sl.created_at)
                ORDER BY hour_mark", cn);

            using var rdByHour = cmdByHour.ExecuteReader();
            while (rdByHour.Read())
            {
                var hour = rdByHour.GetInt32("hour_mark");
                vm.HourlyScrapChart.Labels.Add($"{hour:00}:00");
                vm.HourlyScrapChart.Values.Add(Convert.ToInt32(rdByHour.GetInt64("qty")));
            }
        }

        return vm;
    }

    public GerenciaDailyTrendVm GetDailyTrend()
    {
        var vm = new GerenciaDailyTrendVm();

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(@"
            WITH step_metrics AS (
                SELECT wse.*,
                       GREATEST(
                           CAST(COALESCE(LAG(wse.qty_in) OVER (PARTITION BY wse.wip_item_id ORDER BY wse.create_at, wse.id), wse.qty_in) AS SIGNED) - CAST(wse.qty_in AS SIGNED),
                           0
                       ) AS calc_scrap
                FROM wip_step_execution wse
            )
            SELECT DATE(create_at) AS day,
                   COALESCE(SUM(GREATEST(CAST(qty_in AS SIGNED) - CAST(calc_scrap AS SIGNED), 0)), 0) AS qty
            FROM step_metrics
            GROUP BY DATE(create_at)
            ORDER BY day DESC
            LIMIT 30", cn);

        using var rd = cmd.ExecuteReader();
        var rows = new List<(DateTime Day, int Qty)>();
        while (rd.Read())
        {
            rows.Add((rd.GetDateTime("day"), Convert.ToInt32(rd.GetInt64("qty"))));
        }

        rows.Reverse();
        foreach (var row in rows)
        {
            vm.TrendChart.Labels.Add(row.Day.ToString("MM-dd"));
            vm.TrendChart.Values.Add(row.Qty);
        }

        return vm;
    }

    public GerenciaProductionVm GetProduction()
    {
        var vm = new GerenciaProductionVm();

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        var production = LoadProductionByLocation(cn);
        vm.ProductionByLocation.AddRange(production);

        foreach (var item in production)
        {
            vm.ProductionByLocationChart.Labels.Add(item.Location);
            vm.ProductionByLocationChart.Values.Add(item.QtyProduced);
        }

        return vm;
    }

    public GerenciaWorkOrdersVm GetWorkOrders()
    {
        var vm = new GerenciaWorkOrdersVm();

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        vm.ActiveWorkOrders.AddRange(LoadWorkOrdersByStatus(cn, "OPEN"));
        vm.InProgressWorkOrders.AddRange(LoadWorkOrdersByStatus(cn, "IN_PROGRESS"));
        vm.CancelledWorkOrders.AddRange(LoadWorkOrdersByStatus(cn, "CANCELLED"));
        vm.DelayedWorkOrders.AddRange(LoadDelayedWorkOrders(cn));

        return vm;
    }

    public GerenciaWipVm GetWipOverview()
    {
        var vm = new GerenciaWipVm();

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        LoadWipStatusChart(cn, vm.WipStatusChart);
        vm.WipItems.AddRange(LoadWipItems(cn));

        return vm;
    }

    public GerenciaScanEventsVm GetScanEvents()
    {
        var vm = new GerenciaScanEventsVm();

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        LoadScanEventChart(cn, vm.ScanEventChart);
        vm.RecentEvents.AddRange(LoadRecentScanEvents(cn));

        return vm;
    }

    public GerenciaThroughputVm GetThroughput()
    {
        var vm = new GerenciaThroughputVm();

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(@"
            WITH step_metrics AS (
                SELECT wse.*,
                       GREATEST(
                           CAST(COALESCE(LAG(wse.qty_in) OVER (PARTITION BY wse.wip_item_id ORDER BY wse.create_at, wse.id), wse.qty_in) AS SIGNED) - CAST(wse.qty_in AS SIGNED),
                           0
                       ) AS calc_scrap
                FROM wip_step_execution wse
            )
            SELECT DATE(create_at) AS day,
                   SUM(GREATEST(CAST(qty_in AS SIGNED) - CAST(calc_scrap AS SIGNED), 0)) AS qty
            FROM step_metrics
            GROUP BY DATE(create_at)
            ORDER BY day DESC
            LIMIT 14", cn);

        using var rd = cmd.ExecuteReader();
        var rows = new List<GerenciaThroughputVm.Row>();
        while (rd.Read())
        {
            rows.Add(new GerenciaThroughputVm.Row
            {
                Day = rd.GetDateTime("day"),
                QtyProduced = Convert.ToInt32(rd.GetInt64("qty"))
            });
        }

        rows.Reverse();
        foreach (var item in rows)
        {
            vm.Items.Add(item);
            vm.DailyThroughputChart.Labels.Add(item.Day.ToString("MM-dd"));
            vm.DailyThroughputChart.Values.Add(item.QtyProduced);
        }

        return vm;
    }

    public GerenciaReworkSummaryVm GetReworkSummary()
    {
        var vm = new GerenciaReworkSummaryVm();

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(@"
            SELECT l.name AS location_name,
                   SUM(wrl.qty) AS qty,
                   COUNT(*) AS events
            FROM wip_rework_log wrl
            JOIN location l ON l.id = wrl.location_id
            GROUP BY l.id, l.name
            ORDER BY qty DESC", cn);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var row = new GerenciaReworkSummaryVm.Row
            {
                Location = rd.GetString("location_name"),
                Qty = Convert.ToInt32(rd.GetInt64("qty")),
                Events = Convert.ToInt32(rd.GetInt64("events"))
            };
            vm.Items.Add(row);
            vm.ReworkByLocationChart.Labels.Add(row.Location);
            vm.ReworkByLocationChart.Values.Add(row.Qty);
        }

        return vm;
    }

    public GerenciaWoHealthVm GetWoHealth()
    {
        var vm = new GerenciaWoHealthVm();

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(@"
            SELECT wo.status,
                   COUNT(*) AS total,
                   SUM(CASE WHEN wip.id IS NULL THEN 0 ELSE 1 END) AS with_wip
            FROM work_order wo
            LEFT JOIN wip_item wip ON wip.wo_order_id = wo.id
            GROUP BY wo.status
            ORDER BY wo.status", cn);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var row = new GerenciaWoHealthVm.Row
            {
                Status = rd.GetString("status"),
                Total = Convert.ToInt32(rd.GetInt64("total")),
                WithWip = Convert.ToInt32(rd.GetInt64("with_wip"))
            };
            vm.Items.Add(row);
            vm.StatusChart.Labels.Add(row.Status);
            vm.StatusChart.Values.Add(row.Total);
        }

        return vm;
    }

    private static List<LocationProductionVm> LoadProductionByLocation(MySqlConnection cn)
    {
        var items = new List<LocationProductionVm>();

        using var cmd = new MySqlCommand(@"
            WITH step_metrics AS (
                SELECT wse.*,
                       GREATEST(
                           CAST(COALESCE(LAG(wse.qty_in) OVER (PARTITION BY wse.wip_item_id ORDER BY wse.create_at, wse.id), wse.qty_in) AS SIGNED) - CAST(wse.qty_in AS SIGNED),
                           0
                       ) AS calc_scrap
                FROM wip_step_execution wse
            )
            SELECT l.name,
                   COALESCE(SUM(GREATEST(CAST(sm.qty_in AS SIGNED) - CAST(sm.calc_scrap AS SIGNED), 0)), 0) AS qty
            FROM location l
            LEFT JOIN step_metrics sm ON sm.location_id = l.id
            GROUP BY l.id, l.name
            ORDER BY qty DESC, l.name", cn);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            items.Add(new LocationProductionVm
            {
                Location = rd.GetString("name"),
                QtyProduced = Convert.ToInt32(rd.GetInt64("qty"))
            });
        }

        return items;
    }

    private static WeeklyOutputMatrixVm GetWeeklyOutputMatrix(MySqlConnection cn, DateTime startDate, DateTime endDate, string? sortBy)
    {
        var matrix = new WeeklyOutputMatrixVm
        {
            StartDate = startDate,
            EndDate = endDate
        };

        var subfamilyNames = new List<string>();
        using (var subfamilyCmd = new MySqlCommand(@"
            SELECT DISTINCT COALESCE(s.name, 'Sin subfamilia') AS subfamily_name
            FROM wip_step_execution wse
            JOIN wip_item wip ON wip.id = wse.wip_item_id
            JOIN work_order wo ON wo.id = wip.wo_order_id
            JOIN product p ON p.id = wo.product_id
            LEFT JOIN subfamily s ON s.id = p.id_subfamily
            WHERE DATE(wse.create_at) BETWEEN @startDate AND @endDate
            ORDER BY subfamily_name", cn))
        {
            subfamilyCmd.Parameters.AddWithValue("@startDate", startDate);
            subfamilyCmd.Parameters.AddWithValue("@endDate", endDate);

            using var subfamilyRd = subfamilyCmd.ExecuteReader();
            while (subfamilyRd.Read())
            {
                subfamilyNames.Add(subfamilyRd.GetString("subfamily_name"));
            }
        }

        matrix.Subfamilies.AddRange(subfamilyNames);

        var map = new Dictionary<string, (int Qty, int Scrap)>(StringComparer.OrdinalIgnoreCase);
        using (var dataCmd = new MySqlCommand(@"
            WITH step_metrics AS (
                SELECT wse.*,
                       GREATEST(
                           CAST(COALESCE(LAG(wse.qty_in) OVER (PARTITION BY wse.wip_item_id ORDER BY wse.create_at, wse.id), wse.qty_in) AS SIGNED) - CAST(wse.qty_in AS SIGNED),
                           0
                       ) AS calc_scrap
                FROM wip_step_execution wse
            )
            SELECT DATE(sm.create_at) AS day,
                   COALESCE(s.name, 'Sin subfamilia') AS subfamily_name,
                   COALESCE(SUM(GREATEST(CAST(sm.qty_in AS SIGNED) - CAST(sm.calc_scrap AS SIGNED), 0)), 0) AS qty_produced,
                   COALESCE(SUM(sm.calc_scrap), 0) AS qty_scrap
            FROM step_metrics sm
            JOIN wip_item wip ON wip.id = sm.wip_item_id
            JOIN work_order wo ON wo.id = wip.wo_order_id
            JOIN product p ON p.id = wo.product_id
            LEFT JOIN subfamily s ON s.id = p.id_subfamily
            WHERE DATE(sm.create_at) BETWEEN @startDate AND @endDate
            GROUP BY DATE(sm.create_at), subfamily_name", cn))
        {
            dataCmd.Parameters.AddWithValue("@startDate", startDate);
            dataCmd.Parameters.AddWithValue("@endDate", endDate);

            using var rd = dataCmd.ExecuteReader();
            while (rd.Read())
            {
                var key = $"{rd.GetDateTime("day"):yyyy-MM-dd}|{rd.GetString("subfamily_name")}";
                map[key] = (
                    Qty: Convert.ToInt32(rd.GetInt64("qty_produced")),
                    Scrap: Convert.ToInt32(rd.GetInt64("qty_scrap")));
            }
        }

        var detailMap = new Dictionary<string, List<WeeklyOutputOrderDetailVm>>(StringComparer.OrdinalIgnoreCase);
        using (var detailCmd = new MySqlCommand(@"
            WITH step_metrics AS (
                SELECT wse.*,
                       GREATEST(
                           CAST(COALESCE(LAG(wse.qty_in) OVER (PARTITION BY wse.wip_item_id ORDER BY wse.create_at, wse.id), wse.qty_in) AS SIGNED) - CAST(wse.qty_in AS SIGNED),
                           0
                       ) AS calc_scrap
                FROM wip_step_execution wse
            )
            SELECT DATE(sm.create_at) AS day,
                   COALESCE(s.name, 'Sin subfamilia') AS subfamily_name,
                   wo.wo_number,
                   p.part_number,
                   MIN(wip.created_at) AS wip_start_at,
                   l.name AS location_name,
                   COALESCE(SUM(GREATEST(CAST(sm.qty_in AS SIGNED) - CAST(sm.calc_scrap AS SIGNED), 0)), 0) AS qty_produced,
                   COALESCE(SUM(sm.calc_scrap), 0) AS qty_scrap
            FROM step_metrics sm
            JOIN wip_item wip ON wip.id = sm.wip_item_id
            JOIN work_order wo ON wo.id = wip.wo_order_id
            JOIN product p ON p.id = wo.product_id
            LEFT JOIN subfamily s ON s.id = p.id_subfamily
            LEFT JOIN location l ON l.id = sm.location_id
            WHERE DATE(sm.create_at) BETWEEN @startDate AND @endDate
            GROUP BY DATE(sm.create_at), subfamily_name, wo.wo_number, p.part_number, location_name
            ORDER BY day, subfamily_name, " + BuildSortSql(sortBy), cn))
        {
            detailCmd.Parameters.AddWithValue("@startDate", startDate);
            detailCmd.Parameters.AddWithValue("@endDate", endDate);

            using var rd = detailCmd.ExecuteReader();
            while (rd.Read())
            {
                var key = $"{rd.GetDateTime("day"):yyyy-MM-dd}|{rd.GetString("subfamily_name")}";
                if (!detailMap.TryGetValue(key, out var list))
                {
                    list = new List<WeeklyOutputOrderDetailVm>();
                    detailMap[key] = list;
                }

                var locationOrdinal = rd.GetOrdinal("location_name");
                var startOrdinal = rd.GetOrdinal("wip_start_at");

                list.Add(new WeeklyOutputOrderDetailVm
                {
                    WoNumber = rd.GetString("wo_number"),
                    Product = rd.GetString("part_number"),
                    Location = rd.IsDBNull(locationOrdinal) ? null : rd.GetString(locationOrdinal),
                    WipStartAt = rd.IsDBNull(startOrdinal) ? null : rd.GetDateTime(startOrdinal),
                    Qty = Convert.ToInt32(rd.GetInt64("qty_produced")),
                    Scrap = Convert.ToInt32(rd.GetInt64("qty_scrap"))
                });
            }
        }

        var countMap = new Dictionary<string, (int OrdersCount, int ProductsCount)>(StringComparer.OrdinalIgnoreCase);
        using (var countCmd = new MySqlCommand(@"
            WITH step_metrics AS (
                SELECT wse.*
                FROM wip_step_execution wse
            )
            SELECT DATE(sm.create_at) AS day,
                   COALESCE(s.name, 'Sin subfamilia') AS subfamily_name,
                   COUNT(DISTINCT wo.id) AS orders_count,
                   COUNT(DISTINCT p.id) AS products_count
            FROM step_metrics sm
            JOIN wip_item wip ON wip.id = sm.wip_item_id
            JOIN work_order wo ON wo.id = wip.wo_order_id
            JOIN product p ON p.id = wo.product_id
            LEFT JOIN subfamily s ON s.id = p.id_subfamily
            WHERE DATE(sm.create_at) BETWEEN @startDate AND @endDate
            GROUP BY DATE(sm.create_at), subfamily_name", cn))
        {
            countCmd.Parameters.AddWithValue("@startDate", startDate);
            countCmd.Parameters.AddWithValue("@endDate", endDate);

            using var rd = countCmd.ExecuteReader();
            while (rd.Read())
            {
                var key = $"{rd.GetDateTime("day"):yyyy-MM-dd}|{rd.GetString("subfamily_name")}";
                countMap[key] = (
                    OrdersCount: Convert.ToInt32(rd.GetInt64("orders_count")),
                    ProductsCount: Convert.ToInt32(rd.GetInt64("products_count")));
            }
        }

        for (var day = startDate.Date; day <= endDate.Date; day = day.AddDays(1))
        {
            var row = new WeeklyOutputDayRowVm { Day = day };

            foreach (var subfamily in subfamilyNames)
            {
                var key = $"{day:yyyy-MM-dd}|{subfamily}";
                var values = map.TryGetValue(key, out var found) ? found : (Qty: 0, Scrap: 0);
                var details = detailMap.TryGetValue(key, out var detailRows) ? detailRows : new List<WeeklyOutputOrderDetailVm>();
                var counts = countMap.TryGetValue(key, out var foundCounts) ? foundCounts : (OrdersCount: 0, ProductsCount: 0);

                row.Cells.Add(new WeeklyOutputCellVm
                {
                    Subfamily = subfamily,
                    Qty = values.Qty,
                    Scrap = values.Scrap,
                    OrdersCount = counts.OrdersCount,
                    ProductsCount = counts.ProductsCount,
                    Details = details
                });

                row.TotalQty += values.Qty;
                row.TotalScrap += values.Scrap;
            }

            matrix.Rows.Add(row);
        }

        return matrix;
    }

    private static DiscreteInventoryMatrixVm GetDiscreteInventoryMatrix(MySqlConnection cn, DateTime startDate, DateTime endDate, GerenciaDiscreteMapVm vm)
    {
        var matrix = new DiscreteInventoryMatrixVm
        {
            StartDate = startDate,
            EndDate = endDate
        };

        var locationNames = new List<string>();
        using (var locationCmd = new MySqlCommand(@"
            SELECT DISTINCT l.name AS location_name
            FROM route r
            JOIN route_step rs ON rs.route_id = r.id
            JOIN location l ON l.id = rs.location_id
            WHERE r.active = 1
            ORDER BY l.name", cn))
        {
            using var locationRd = locationCmd.ExecuteReader();
            while (locationRd.Read())
            {
                locationNames.Add(locationRd.GetString("location_name"));
            }
        }

        var allSubfamilyNames = new List<string>();
        using (var subfamilyCmd = new MySqlCommand(@"
            SELECT DISTINCT COALESCE(sf.name, 'Sin subfamilia') AS subfamily_name
            FROM route r
            JOIN subfamily sf ON sf.id = r.subfamily_id
            WHERE r.active = 1
            ORDER BY subfamily_name", cn))
        {
            using var subfamilyRd = subfamilyCmd.ExecuteReader();
            while (subfamilyRd.Read())
            {
                allSubfamilyNames.Add(subfamilyRd.GetString("subfamily_name"));
            }
        }

        var periodMap = new Dictionary<string, (int Pieces, int Orders)>(StringComparer.OrdinalIgnoreCase);
        using (var periodCmd = new MySqlCommand(@"
            WITH active_locations AS (
                SELECT DISTINCT rs.location_id
                FROM route r
                JOIN route_step rs ON rs.route_id = r.id
                WHERE r.active = 1
            )
            SELECT COALESCE(sf.name, 'Sin subfamilia') AS subfamily_name,
                   l.name AS location_name,
                   COALESCE(SUM(wse.qty_in), 0) AS pieces_total,
                   COUNT(DISTINCT wo.id) AS orders_total
            FROM wip_step_execution wse
            JOIN route_step rs ON rs.id = wse.route_step_id
            JOIN active_locations al ON al.location_id = rs.location_id
            JOIN location l ON l.id = rs.location_id
            JOIN wip_item wip ON wip.id = wse.wip_item_id
            JOIN work_order wo ON wo.id = wip.wo_order_id
            JOIN product p ON p.id = wo.product_id
            LEFT JOIN subfamily sf ON sf.id = p.id_subfamily
            WHERE DATE(wse.create_at) BETWEEN @startDate AND @endDate
            GROUP BY subfamily_name, location_name", cn))
        {
            periodCmd.Parameters.AddWithValue("@startDate", startDate.Date);
            periodCmd.Parameters.AddWithValue("@endDate", endDate.Date);

            using var periodRd = periodCmd.ExecuteReader();
            while (periodRd.Read())
            {
                var key = $"{periodRd.GetString("location_name")}|{periodRd.GetString("subfamily_name")}";
                periodMap[key] = (
                    Pieces: Convert.ToInt32(periodRd.GetInt64("pieces_total")),
                    Orders: Convert.ToInt32(periodRd.GetInt64("orders_total")));
            }
        }

        var visibleSubfamilies = allSubfamilyNames;

        matrix.Subfamilies.AddRange(visibleSubfamilies);
        vm.HiddenSubfamilies.Clear();

        foreach (var location in locationNames)
        {
            var row = new DiscreteInventoryLocationRowVm
            {
                Location = location
            };

            foreach (var subfamily in visibleSubfamilies)
            {
                var key = $"{location}|{subfamily}";
                var values = periodMap.TryGetValue(key, out var found) ? found : (Pieces: 0, Orders: 0);

                row.Cells.Add(new DiscreteInventoryCellVm
                {
                    Subfamily = subfamily,
                    Pieces = values.Pieces,
                    Orders = values.Orders
                });

                row.TotalPieces += values.Pieces;
                row.TotalOrders += values.Orders;
            }

            matrix.Rows.Add(row);
        }

        return matrix;
    }

    private static string NormalizeMetricView(string? metricView)
    {
        return metricView?.ToLowerInvariant() switch
        {
            "orders" => "orders",
            "products" => "products",
            _ => "pieces"
        };
    }

    private static string NormalizeLocationName(string locationName)
    {
        return locationName.Trim().ToLowerInvariant() switch
        {
            "inspeccion final" => "Inspeccion Final",
            "inspección final" => "Inspeccion Final",
            "prueba electrica" => "Prueba Electrica",
            "prueba eléctrica" => "Prueba Electrica",
            "tie bar" => "Tie Bar",
            "tin plate" => "Tin Plate",
            "alloy" => "Alloy",
            "backfill" => "Backfill",
            "fastcast" => "Fastcast",
            "moldeo" => "Moldeo",
            "empaque" => "Empaque",
            "calidad" => "Calidad",
            _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(locationName.Trim().ToLowerInvariant())
        };
    }


    private static string NormalizeQuickRange(string? periodType)
    {
        return periodType?.ToLowerInvariant() switch
        {
            "all_day" or "day" => "day",
            "all_week" or "week" => "week",
            "all_month" or "month" => "month",
            "all_year" or "year" or "historic" => "historic",
            _ => "day"
        };
    }

    private static string? ResolveSelectedSubfamily(List<string> subfamilies, string? selectedSubfamily)
    {
        if (subfamilies.Count == 0) return null;
        if (!string.IsNullOrWhiteSpace(selectedSubfamily) && subfamilies.Contains(selectedSubfamily))
        {
            return selectedSubfamily;
        }

        return subfamilies[0];
    }

    private static void LoadSubfamilyTopProducts(MySqlConnection cn, GerenciaDiscreteMapVm vm, DateTime startDate, DateTime endDate)
    {
        if (string.IsNullOrWhiteSpace(vm.SelectedSubfamily))
        {
            return;
        }

        using var cmd = new MySqlCommand(@"
            WITH step_metrics AS (
                SELECT wse.*,
                       GREATEST(
                           CAST(COALESCE(LAG(wse.qty_in) OVER (PARTITION BY wse.wip_item_id ORDER BY wse.create_at, wse.id), wse.qty_in) AS SIGNED) - CAST(wse.qty_in AS SIGNED),
                           0
                       ) AS calc_scrap
                FROM wip_step_execution wse
            )
            SELECT p.part_number,
                   COALESCE(SUM(GREATEST(CAST(sm.qty_in AS SIGNED) - CAST(sm.calc_scrap AS SIGNED), 0)), 0) AS qty_produced,
                   COALESCE(SUM(sm.calc_scrap), 0) AS qty_scrap,
                   COUNT(DISTINCT wo.id) AS orders_count
            FROM step_metrics sm
            JOIN wip_item wip ON wip.id = sm.wip_item_id
            JOIN work_order wo ON wo.id = wip.wo_order_id
            JOIN product p ON p.id = wo.product_id
            LEFT JOIN subfamily s ON s.id = p.id_subfamily
            WHERE DATE(sm.create_at) BETWEEN @startDate AND @endDate
              AND COALESCE(s.name, 'Sin subfamilia') = @subfamily
            GROUP BY p.id, p.part_number
            ORDER BY qty_produced DESC, orders_count DESC, p.part_number
            LIMIT 10", cn);

        cmd.Parameters.AddWithValue("@startDate", startDate);
        cmd.Parameters.AddWithValue("@endDate", endDate);
        cmd.Parameters.AddWithValue("@subfamily", vm.SelectedSubfamily);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var row = new SubfamilyProductStatVm
            {
                Product = rd.GetString("part_number"),
                Qty = Convert.ToInt32(rd.GetInt64("qty_produced")),
                Scrap = Convert.ToInt32(rd.GetInt64("qty_scrap")),
                Orders = Convert.ToInt32(rd.GetInt64("orders_count"))
            };

            vm.SubfamilyTopProducts.Add(row);
            vm.SubfamilyTopProductsChart.Labels.Add(row.Product);
            vm.SubfamilyTopProductsChart.Values.Add(row.Qty);
        }
    }

    private static void LoadDiscreteOrdersSummary(MySqlConnection cn, GerenciaDiscreteMapVm vm, DateTime startDate, DateTime endDate)
    {
        using var cmd = new MySqlCommand(@"
            WITH active_orders AS (
                SELECT DISTINCT wo.id, wo.status
                FROM work_order wo
                JOIN wip_item wip ON wip.wo_order_id = wo.id
                JOIN wip_step_execution wse ON wse.wip_item_id = wip.id
                WHERE DATE(wse.create_at) BETWEEN @startDate AND @endDate
            )
            SELECT
                (SELECT COUNT(*) FROM active_orders WHERE status = 'OPEN') AS open_total,
                (SELECT COUNT(*) FROM active_orders WHERE status = 'IN_PROGRESS') AS in_progress_total,
                (SELECT COUNT(*) FROM active_orders WHERE status = 'CANCELLED') AS cancelled_total,
                (SELECT COUNT(*) FROM active_orders WHERE status = 'FINISHED') AS finished_total", cn);

        cmd.Parameters.AddWithValue("@startDate", startDate.Date);
        cmd.Parameters.AddWithValue("@endDate", endDate.Date);

        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return;

        vm.OrdersSummaryChart.Labels.AddRange(["Abiertas", "En Progreso", "Canceladas", "Terminadas"]);
        vm.OrdersSummaryChart.Values.AddRange([
            Convert.ToInt32(rd.GetInt64("open_total")),
            Convert.ToInt32(rd.GetInt64("in_progress_total")),
            Convert.ToInt32(rd.GetInt64("cancelled_total")),
            Convert.ToInt32(rd.GetInt64("finished_total"))
        ]);
    }

    private static void LoadDiscreteProductionVsScrap(MySqlConnection cn, GerenciaDiscreteMapVm vm, DateTime startDate, DateTime endDate)
    {
        using var cmd = new MySqlCommand(@"
            WITH step_metrics AS (
                SELECT DATE(wse.create_at) AS metric_day,
                       wse.qty_in,
                       GREATEST(
                           CAST(COALESCE(LAG(wse.qty_in) OVER (PARTITION BY wse.wip_item_id ORDER BY wse.create_at, wse.id), wse.qty_in) AS SIGNED) - CAST(wse.qty_in AS SIGNED),
                           0
                       ) AS calc_scrap,
                       COALESCE(sf.name, 'Sin subfamilia') AS subfamily_name
                FROM wip_step_execution wse
                JOIN wip_item wip ON wip.id = wse.wip_item_id
                JOIN work_order wo ON wo.id = wip.wo_order_id
                JOIN product p ON p.id = wo.product_id
                LEFT JOIN subfamily sf ON sf.id = p.id_subfamily
                WHERE DATE(wse.create_at) BETWEEN @startDate AND @endDate
                  AND (@subfamily IS NULL OR COALESCE(sf.name, 'Sin subfamilia') = @subfamily)
            )
            SELECT metric_day,
                   COALESCE(SUM(GREATEST(CAST(qty_in AS SIGNED) - CAST(calc_scrap AS SIGNED), 0)), 0) AS produced_total,
                   COALESCE(SUM(calc_scrap), 0) AS scrap_total
            FROM step_metrics
            GROUP BY metric_day
            ORDER BY metric_day", cn);

        cmd.Parameters.AddWithValue("@startDate", startDate.Date);
        cmd.Parameters.AddWithValue("@endDate", endDate.Date);
        cmd.Parameters.AddWithValue("@subfamily", string.IsNullOrWhiteSpace(vm.SelectedSubfamily) ? DBNull.Value : vm.SelectedSubfamily);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var day = rd.GetDateTime("metric_day");
            var produced = Convert.ToInt32(rd.GetInt64("produced_total"));
            var scrap = Convert.ToInt32(rd.GetInt64("scrap_total"));

            vm.ProductionTrendChart.Labels.Add(day.ToString("MM-dd"));
            vm.ProductionTrendChart.Values.Add(produced);
            vm.ScrapTrendChart.Labels.Add(day.ToString("MM-dd"));
            vm.ScrapTrendChart.Values.Add(scrap);
            vm.ProducedTotal += produced;
            vm.ScrapTotal += scrap;
        }

        vm.TotalsComparisonChart.Labels.AddRange(["Piezas", "Órdenes"]);
        vm.TotalsComparisonChart.Values.Add(vm.Matrix.TotalPieces);
        vm.TotalsComparisonChart.Values.Add(vm.Matrix.TotalOrders);
    }

    private static void ResolvePeriod(string periodType, string? weekValue, string? monthValue, DateTime? fromDate, DateTime? toDate, out DateTime start, out DateTime end, out string normalizedPeriodType, out string normalizedWeek, out string normalizedMonth)
    {
        var today = DateTime.UtcNow.Date;
        normalizedPeriodType = periodType ?? "week";

        switch (normalizedPeriodType)
        {
            case "day":
                start = today;
                end = start;
                break;
            case "month":
                if (!string.IsNullOrWhiteSpace(monthValue) && DateTime.TryParse($"{monthValue}-01", out var monthDate))
                {
                    start = new DateTime(monthDate.Year, monthDate.Month, 1);
                }
                else
                {
                    start = new DateTime(today.Year, today.Month, 1);
                }
                end = start.AddMonths(1).AddDays(-1);
                break;
            case "historic":
                start = new DateTime(2020, 1, 1);
                end = today;
                break;
            case "custom" when fromDate.HasValue && toDate.HasValue:
                start = fromDate.Value.Date;
                end = toDate.Value.Date;
                if (end < start) (start, end) = (end, start);
                break;
            case "week":
            default:
                if (!string.IsNullOrWhiteSpace(weekValue) && TryParseWeekValue(weekValue, out var weekStart))
                {
                    start = weekStart;
                }
                else
                {
                    start = GetStartOfWeek(today);
                }
                end = start.AddDays(6);
                if (normalizedPeriodType != "custom") normalizedPeriodType = "week";
                break;
        }

        normalizedWeek = $"{ISOWeek.GetYear(start)}-W{ISOWeek.GetWeekOfYear(start):00}";
        normalizedMonth = $"{start:yyyy-MM}";
    }

    private static void LoadDashboardExecutiveSummary(MySqlConnection cn, GerenciaDashboardVm vm)
    {
        vm.SnapshotAtUtc = DateTime.UtcNow;
        var today = vm.SnapshotAtUtc.Date;
        var weekStart = GetStartOfWeek(today);
        var previousWeekStart = weekStart.AddDays(-7);
        var previousSameDay = today.AddDays(-7);

        using var cmd = new MySqlCommand(@"
            WITH wo_birth AS (
                SELECT wo.id,
                       DATE(COALESCE(MIN(wip.created_at), UTC_TIMESTAMP())) AS born_day
                FROM work_order wo
                LEFT JOIN wip_item wip ON wip.wo_order_id = wo.id
                GROUP BY wo.id
            )
            SELECT
                (SELECT COUNT(*) FROM wo_birth WHERE born_day = @today) AS new_today,
                (SELECT COUNT(*) FROM wo_birth WHERE born_day = @previousSameDay) AS prev_same_day,
                (SELECT COUNT(*) FROM wo_birth WHERE born_day BETWEEN @weekStart AND @today) AS week_to_date,
                (SELECT COUNT(*) FROM wo_birth WHERE born_day BETWEEN @previousWeekStart AND @previousSameDay) AS previous_week_to_date,
                (SELECT COUNT(*) FROM work_order WHERE status IN ('OPEN','IN_PROGRESS','FINISHED','CANCELLED','HOLD')) AS on_floor_total,
                (SELECT COUNT(*) FROM work_order WHERE status = 'OPEN') AS open_total,
                (SELECT COUNT(*) FROM work_order WHERE status = 'FINISHED') AS finished_total,
                (SELECT COUNT(*) FROM work_order WHERE status = 'CANCELLED') AS cancelled_total,
                (SELECT COUNT(*) FROM work_order WHERE status = 'HOLD') AS hold_total", cn);

        cmd.Parameters.AddWithValue("@today", today);
        cmd.Parameters.AddWithValue("@previousSameDay", previousSameDay);
        cmd.Parameters.AddWithValue("@weekStart", weekStart);
        cmd.Parameters.AddWithValue("@previousWeekStart", previousWeekStart);

        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return;

        vm.NewOrdersToday = Convert.ToInt32(rd.GetInt64("new_today"));
        vm.PreviousWeekSameDayNewOrders = Convert.ToInt32(rd.GetInt64("prev_same_day"));
        var weekToDate = Convert.ToInt32(rd.GetInt64("week_to_date"));
        var previousWeekToDate = Convert.ToInt32(rd.GetInt64("previous_week_to_date"));
        vm.OnFloorTotal = Convert.ToInt32(rd.GetInt64("on_floor_total"));
        vm.OpenOrdersCount = Convert.ToInt32(rd.GetInt64("open_total"));
        vm.FinishedOrdersCount = Convert.ToInt32(rd.GetInt64("finished_total"));
        vm.CancelledOrdersCount = Convert.ToInt32(rd.GetInt64("cancelled_total"));
        vm.HoldOrdersCount = Convert.ToInt32(rd.GetInt64("hold_total"));

        vm.DayRatioPercent = ComputeRatio(vm.NewOrdersToday, vm.PreviousWeekSameDayNewOrders);
        vm.DayRatioUp = vm.DayRatioPercent >= 0;
        vm.WeekRatioPercent = ComputeRatio(weekToDate, previousWeekToDate);
        vm.WeekRatioUp = vm.WeekRatioPercent >= 0;
    }

    private static void LoadLocationBacklogChart(MySqlConnection cn, GerenciaDashboardVm vm)
    {
        using var cmd = new MySqlCommand(@"
            SELECT COALESCE(l.name, 'Sin localidad') AS location_name,
                   COUNT(*) AS pending_orders
            FROM work_order wo
            JOIN wip_item wip ON wip.wo_order_id = wo.id
            LEFT JOIN route_step rs ON rs.id = wip.current_step_id
            LEFT JOIN location l ON l.id = rs.location_id
            LEFT JOIN (
                SELECT se.wip_item_id, MAX(se.ts) AS last_scan
                FROM scan_event se
                GROUP BY se.wip_item_id
            ) ls ON ls.wip_item_id = wip.id
            WHERE wo.status IN ('OPEN', 'IN_PROGRESS', 'HOLD')
              AND COALESCE(ls.last_scan, wip.created_at) <= DATE_SUB(UTC_TIMESTAMP(), INTERVAL 4 DAY)
            GROUP BY location_name
            ORDER BY pending_orders DESC, location_name
            LIMIT 8", cn);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            vm.BacklogByLocationChart.Labels.Add(rd.GetString("location_name"));
            vm.BacklogByLocationChart.Values.Add(Convert.ToInt32(rd.GetInt64("pending_orders")));
        }
    }

    private static decimal ComputeRatio(int current, int baseline)
    {
        if (baseline == 0) return current == 0 ? 0 : 100;
        return Math.Round(((decimal)(current - baseline) / baseline) * 100m, 1);
    }

    private static DateTime GetStartOfWeek(DateTime date)
    {
        var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.AddDays(-diff).Date;
    }

    private static bool TryParseWeekValue(string weekValue, out DateTime weekStart)
    {
        weekStart = DateTime.MinValue;
        var parts = weekValue.Split("-W");
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], out var year)) return false;
        if (!int.TryParse(parts[1], out var week)) return false;

        try
        {
            weekStart = ISOWeek.ToDateTime(year, week, DayOfWeek.Monday);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildSortSql(string? sortBy)
    {
        return (sortBy ?? "fifo").ToLowerInvariant() switch
        {
            "qty_desc" => "qty_produced DESC, wo.wo_number",
            "scrap_desc" => "qty_scrap DESC, wo.wo_number",
            "wo" => "wo.wo_number",
            _ => "wip_start_at ASC, wo.wo_number"
        };
    }

    private static void LoadWorkOrderStatusChart(MySqlConnection cn, ChartVm chart)
    {
        using var cmd = new MySqlCommand(@"
            SELECT status, COUNT(*) AS total
            FROM work_order
            GROUP BY status
            ORDER BY status", cn);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            chart.Labels.Add(rd.GetString("status"));
            chart.Values.Add(Convert.ToInt32(rd.GetInt64("total")));
        }
    }

    private static void LoadWipStatusChart(MySqlConnection cn, ChartVm chart)
    {
        using var cmd = new MySqlCommand(@"
            SELECT status, COUNT(*) AS total
            FROM wip_item
            GROUP BY status
            ORDER BY status", cn);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            chart.Labels.Add(rd.GetString("status"));
            chart.Values.Add(Convert.ToInt32(rd.GetInt64("total")));
        }
    }

    private static void LoadScanEventChart(MySqlConnection cn, ChartVm chart)
    {
        using var cmd = new MySqlCommand(@"
            SELECT scan_type, COUNT(*) AS total
            FROM scan_event
            GROUP BY scan_type
            ORDER BY scan_type", cn);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            chart.Labels.Add(rd.GetString("scan_type"));
            chart.Values.Add(Convert.ToInt32(rd.GetInt64("total")));
        }
    }

    private static List<WorkOrderVm> LoadNewestWorkOrders(MySqlConnection cn, int take)
    {
        var items = new List<WorkOrderVm>();

        using var cmd = new MySqlCommand(@"
            SELECT wo.wo_number,
                   wo.status,
                   p.part_number,
                   wip.id AS wip_id,
                   wip.status AS wip_status,
                   wip.created_at,
                   l.name AS location_name
            FROM work_order wo
            JOIN product p ON p.id = wo.product_id
            LEFT JOIN wip_item wip ON wip.wo_order_id = wo.id
            LEFT JOIN route_step rs ON rs.id = wip.current_step_id
            LEFT JOIN location l ON l.id = rs.location_id
            ORDER BY COALESCE(wip.created_at, NOW()) DESC, wo.id DESC
            LIMIT @take", cn);

        cmd.Parameters.AddWithValue("@take", take);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var wipIdOrdinal = rd.GetOrdinal("wip_id");
            var wipStatusOrdinal = rd.GetOrdinal("wip_status");
            var locationOrdinal = rd.GetOrdinal("location_name");
            var createdAtOrdinal = rd.GetOrdinal("created_at");

            items.Add(new WorkOrderVm
            {
                WoNumber = rd.GetString("wo_number"),
                Status = rd.GetString("status"),
                Product = rd.GetString("part_number"),
                WipItemId = rd.IsDBNull(wipIdOrdinal) ? null : rd.GetUInt32(wipIdOrdinal),
                WipStatus = rd.IsDBNull(wipStatusOrdinal) ? null : rd.GetString(wipStatusOrdinal),
                CurrentLocation = rd.IsDBNull(locationOrdinal) ? null : rd.GetString(locationOrdinal),
                WipCreatedAt = rd.IsDBNull(createdAtOrdinal) ? null : rd.GetDateTime(createdAtOrdinal)
            });
        }

        return items;
    }

    private static List<WorkOrderVm> LoadWorkOrdersByStatus(MySqlConnection cn, string status)
    {
        var items = new List<WorkOrderVm>();

        using var cmd = new MySqlCommand(@"
            SELECT wo.wo_number,
                   wo.status,
                   p.part_number,
                   COALESCE(s.name, 'Sin subfamilia') AS subfamily_name,
                   wip.id AS wip_id,
                   wip.status AS wip_status,
                   wip.created_at,
                   l.name AS location_name,
                   COALESCE(last_qty.qty_in, 0) AS current_qty
            FROM work_order wo
            JOIN product p ON p.id = wo.product_id
            LEFT JOIN subfamily s ON s.id = p.id_subfamily
            LEFT JOIN wip_item wip ON wip.wo_order_id = wo.id
            LEFT JOIN route_step rs ON rs.id = wip.current_step_id
            LEFT JOIN location l ON l.id = rs.location_id
            LEFT JOIN (
                SELECT wse.wip_item_id,
                       wse.qty_in
                FROM wip_step_execution wse
                INNER JOIN (
                    SELECT wip_item_id, MAX(id) AS last_step_id
                    FROM wip_step_execution
                    GROUP BY wip_item_id
                ) latest ON latest.last_step_id = wse.id
            ) last_qty ON last_qty.wip_item_id = wip.id
            WHERE wo.status = @status
              AND wip.created_at <= @dataCutoffUtc
              AND NOT (
                    (
                        CASE
                            WHEN l.id = 8 OR COALESCE(l.name, '') LIKE '%Backfill%' THEN 'Backfill'
                            WHEN COALESCE(l.name, '') LIKE '%Fast%' THEN 'FAST CAST'
                            WHEN COALESCE(l.name, '') LIKE '%Emp%' THEN 'Empaque'
                            WHEN COALESCE(l.name, '') LIKE '%QC%' OR COALESCE(l.name, '') LIKE '%Q.C.%' OR COALESCE(l.name, '') LIKE '%Calidad%' THEN 'QC'
                            WHEN COALESCE(l.name, '') LIKE '%Prueba%' THEN 'Prueba Electrica'
                            ELSE COALESCE(l.name, 'Sin localidad')
                        END
                    ) = 'Alloy'
                    AND TIMESTAMPDIFF(DAY, wip.created_at, @dataCutoffUtc) > 20
                  )
            ORDER BY wo.wo_number", cn);

        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@dataCutoffUtc", CurrentCutoffUtc);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var wipIdOrdinal = rd.GetOrdinal("wip_id");
            var wipStatusOrdinal = rd.GetOrdinal("wip_status");
            var locationOrdinal = rd.GetOrdinal("location_name");
            var createdAtOrdinal = rd.GetOrdinal("created_at");

            items.Add(new WorkOrderVm
            {
                WoNumber = rd.GetString("wo_number"),
                Status = rd.GetString("status"),
                Product = rd.GetString("part_number"),
                Subfamily = rd.GetString("subfamily_name"),
                Qty = Convert.ToInt32(rd.GetInt64("current_qty")),
                WipItemId = rd.IsDBNull(wipIdOrdinal) ? null : rd.GetUInt32(wipIdOrdinal),
                WipStatus = rd.IsDBNull(wipStatusOrdinal) ? null : rd.GetString(wipStatusOrdinal),
                CurrentLocation = rd.IsDBNull(locationOrdinal) ? null : rd.GetString(locationOrdinal),
                WipCreatedAt = rd.IsDBNull(createdAtOrdinal) ? null : rd.GetDateTime(createdAtOrdinal)
            });
        }

        return items;
    }

    private static List<DelayedWorkOrderVm> LoadDelayedWorkOrders(MySqlConnection cn)
    {
        var items = new List<DelayedWorkOrderVm>();

        using var cmd = new MySqlCommand(@"
            SELECT wo.wo_number,
                   wo.status,
                   p.part_number,
                   wip.id AS wip_id,
                   wip.status AS wip_status,
                   wip.created_at,
                   l.name AS location_name,
                   DATEDIFF(NOW(), wip.created_at) AS days_delayed
            FROM work_order wo
            JOIN product p ON p.id = wo.product_id
            JOIN wip_item wip ON wip.wo_order_id = wo.id
            LEFT JOIN route_step rs ON rs.id = wip.current_step_id
            LEFT JOIN location l ON l.id = rs.location_id
            WHERE wo.status IN ('OPEN', 'IN_PROGRESS')
              AND wip.created_at <= DATE_SUB(NOW(), INTERVAL 7 DAY)
            ORDER BY days_delayed DESC, wo.wo_number", cn);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var locationOrdinal = rd.GetOrdinal("location_name");

            items.Add(new DelayedWorkOrderVm
            {
                WoNumber = rd.GetString("wo_number"),
                Status = rd.GetString("status"),
                Product = rd.GetString("part_number"),
                WipItemId = rd.GetUInt32("wip_id"),
                WipStatus = rd.GetString("wip_status"),
                CurrentLocation = rd.IsDBNull(locationOrdinal) ? null : rd.GetString(locationOrdinal),
                WipCreatedAt = rd.GetDateTime("created_at"),
                DaysDelayed = Convert.ToInt32(rd.GetInt64("days_delayed"))
            });
        }

        return items;
    }

    private static List<WipItemVm> LoadWipItems(MySqlConnection cn)
    {
        var items = new List<WipItemVm>();

        using var cmd = new MySqlCommand(@"
            SELECT wip.id,
                   wip.status,
                   wip.created_at,
                   wo.wo_number,
                   l.name AS location_name
            FROM wip_item wip
            JOIN work_order wo ON wo.id = wip.wo_order_id
            LEFT JOIN route_step rs ON rs.id = wip.current_step_id
            LEFT JOIN location l ON l.id = rs.location_id
            ORDER BY wip.created_at DESC", cn);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var locationOrdinal = rd.GetOrdinal("location_name");

            items.Add(new WipItemVm
            {
                Id = rd.GetUInt32("id"),
                Status = rd.GetString("status"),
                WoNumber = rd.GetString("wo_number"),
                CurrentLocation = rd.IsDBNull(locationOrdinal) ? null : rd.GetString(locationOrdinal),
                CreatedAt = rd.GetDateTime("created_at")
            });
        }

        return items;
    }

    private static List<ScanEventVm> LoadRecentScanEvents(MySqlConnection cn)
    {
        var items = new List<ScanEventVm>();

        using var cmd = new MySqlCommand(@"
            SELECT se.ts,
                   se.scan_type,
                   wip.id AS wip_id,
                   wo.wo_number,
                   l.name AS location_name
            FROM scan_event se
            JOIN wip_item wip ON wip.id = se.wip_item_id
            JOIN work_order wo ON wo.id = wip.wo_order_id
            LEFT JOIN route_step rs ON rs.id = se.route_step_id
            LEFT JOIN location l ON l.id = rs.location_id
            ORDER BY se.ts DESC
            LIMIT 50", cn);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var locationOrdinal = rd.GetOrdinal("location_name");

            items.Add(new ScanEventVm
            {
                Timestamp = rd.GetDateTime("ts"),
                ScanType = rd.GetString("scan_type"),
                WoNumber = rd.GetString("wo_number"),
                WipItemId = rd.GetUInt32("wip_id"),
                Location = rd.IsDBNull(locationOrdinal) ? null : rd.GetString(locationOrdinal)
            });
        }

        return items;
    }
}
