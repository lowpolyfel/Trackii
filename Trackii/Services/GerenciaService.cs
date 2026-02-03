using MySql.Data.MySqlClient;
using Trackii.Models.Gerencia;

namespace Trackii.Services;

public class GerenciaService
{
    private readonly string _conn;

    public GerenciaService(IConfiguration cfg)
    {
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
        vm.TopLocations.AddRange(production
            .OrderByDescending(item => item.QtyProduced)
            .Take(5));
        vm.BottomLocations.AddRange(production
            .OrderBy(item => item.QtyProduced)
            .Take(5));

        foreach (var item in production)
        {
            vm.ProductionByLocationChart.Labels.Add(item.Location);
            vm.ProductionByLocationChart.Values.Add(item.QtyProduced);
        }

        LoadWipStatusChart(cn, vm.WipStatusChart);
        LoadScanEventChart(cn, vm.ScanEventChart);

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

    private static List<LocationProductionVm> LoadProductionByLocation(MySqlConnection cn)
    {
        var items = new List<LocationProductionVm>();

        using var cmd = new MySqlCommand(@"
            SELECT l.name,
                   COALESCE(SUM(wse.qty_in - wse.qty_scrap), 0) AS qty
            FROM location l
            LEFT JOIN wip_step_execution wse ON wse.location_id = l.id
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

    private static List<WorkOrderVm> LoadWorkOrdersByStatus(MySqlConnection cn, string status)
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
            WHERE wo.status = @status
            ORDER BY wo.wo_number", cn);

        cmd.Parameters.AddWithValue("@status", status);

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
