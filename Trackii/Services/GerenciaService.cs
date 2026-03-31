using MySql.Data.MySqlClient;
using System.Globalization;
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
        var vm = new GerenciaDiscreteMapVm
        {
            PeriodType = string.IsNullOrWhiteSpace(periodType) ? "week" : periodType,
            WeekValue = weekValue,
            MonthValue = monthValue,
            FromDate = fromDate,
            ToDate = toDate,
            MetricView = NormalizeMetricView(metricView),
            SelectedSubfamily = selectedSubfamily,
            SortBy = string.IsNullOrWhiteSpace(sortBy) ? "fifo" : sortBy
        };

        ResolvePeriod(vm.PeriodType, vm.WeekValue, vm.MonthValue, vm.FromDate, vm.ToDate, out var start, out var end, out var normalizedPeriodType, out var normalizedWeek, out var normalizedMonth);

        vm.PeriodType = normalizedPeriodType;
        vm.WeekValue = normalizedWeek;
        vm.MonthValue = normalizedMonth;
        vm.SnapshotDate = end;

        using var cn = new MySqlConnection(_conn);
        cn.Open();
        vm.Matrix = GetDiscreteInventoryMatrix(cn, start, end);

        return vm;
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
            var locationOrdinal = rd.GetOrdinal("location_name");
            var startOrdinal = rd.GetOrdinal("wip_start_at");

            vm.Orders.Add(new DailyOrderDetailVm
            {
                WoNumber = rd.GetString("wo_number"),
                Product = rd.GetString("part_number"),
                Subfamily = rd.GetString("subfamily_name"),
                Location = rd.IsDBNull(locationOrdinal) ? null : rd.GetString(locationOrdinal),
                WipStartAt = rd.IsDBNull(startOrdinal) ? null : rd.GetDateTime(startOrdinal),
                Qty = Convert.ToInt32(rd.GetInt64("qty_produced")),
                Scrap = Convert.ToInt32(rd.GetInt64("qty_scrap"))
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

        using var locationCmd = new MySqlCommand(@"
            SELECT l.name AS location_name,
                   COALESCE(SUM(sl.qty), 0) AS qty,
                   COUNT(*) AS events
            FROM scrap_log sl
            JOIN route_step rs ON rs.id = sl.route_step_id
            JOIN location l ON l.id = rs.location_id
            GROUP BY l.id, l.name
            ORDER BY qty DESC, events DESC
            LIMIT 10", cn);

        using var locationRd = locationCmd.ExecuteReader();
        while (locationRd.Read())
        {
            var row = new ScrapLocationVm
            {
                Location = locationRd.GetString("location_name"),
                Qty = Convert.ToInt32(locationRd.GetInt64("qty")),
                Events = Convert.ToInt32(locationRd.GetInt64("events"))
            };

            vm.Locations.Add(row);
            vm.LocationScrapChart.Labels.Add(row.Location);
            vm.LocationScrapChart.Values.Add(row.Qty);
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
                           COALESCE(LAG(wse.qty_in) OVER (PARTITION BY wse.wip_item_id ORDER BY wse.create_at, wse.id), wse.qty_in) - wse.qty_in,
                           0
                       ) AS calc_scrap
                FROM wip_step_execution wse
            )
            SELECT DATE(create_at) AS day,
                   COALESCE(SUM(qty_in - calc_scrap), 0) AS qty
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
                           COALESCE(LAG(wse.qty_in) OVER (PARTITION BY wse.wip_item_id ORDER BY wse.create_at, wse.id), wse.qty_in) - wse.qty_in,
                           0
                       ) AS calc_scrap
                FROM wip_step_execution wse
            )
            SELECT DATE(create_at) AS day,
                   SUM(qty_in - calc_scrap) AS qty
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
                           COALESCE(LAG(wse.qty_in) OVER (PARTITION BY wse.wip_item_id ORDER BY wse.create_at, wse.id), wse.qty_in) - wse.qty_in,
                           0
                       ) AS calc_scrap
                FROM wip_step_execution wse
            )
            SELECT l.name,
                   COALESCE(SUM(sm.qty_in - sm.calc_scrap), 0) AS qty
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
                           COALESCE(LAG(wse.qty_in) OVER (PARTITION BY wse.wip_item_id ORDER BY wse.create_at, wse.id), wse.qty_in) - wse.qty_in,
                           0
                       ) AS calc_scrap
                FROM wip_step_execution wse
            )
            SELECT DATE(sm.create_at) AS day,
                   COALESCE(s.name, 'Sin subfamilia') AS subfamily_name,
                   COALESCE(SUM(sm.qty_in - sm.calc_scrap), 0) AS qty_produced,
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
                           COALESCE(LAG(wse.qty_in) OVER (PARTITION BY wse.wip_item_id ORDER BY wse.create_at, wse.id), wse.qty_in) - wse.qty_in,
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
                   COALESCE(SUM(sm.qty_in - sm.calc_scrap), 0) AS qty_produced,
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

    private static DiscreteInventoryMatrixVm GetDiscreteInventoryMatrix(MySqlConnection cn, DateTime startDate, DateTime endDate)
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

        var subfamilyNames = new List<string>();
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
                subfamilyNames.Add(subfamilyRd.GetString("subfamily_name"));
            }
        }

        matrix.Subfamilies.AddRange(subfamilyNames);

        var snapshotMap = new Dictionary<string, (int Pieces, int Orders)>(StringComparer.OrdinalIgnoreCase);
        using (var snapshotCmd = new MySqlCommand(@"
            WITH active_locations AS (
                SELECT DISTINCT rs.location_id
                FROM route r
                JOIN route_step rs ON rs.route_id = r.id
                WHERE r.active = 1
            ),
            latest_execution AS (
                SELECT wse.wip_item_id,
                       wse.route_step_id,
                       wse.qty_in,
                       ROW_NUMBER() OVER (PARTITION BY wse.wip_item_id ORDER BY wse.create_at DESC, wse.id DESC) AS rn
                FROM wip_step_execution wse
                WHERE wse.create_at < @snapshotCutoff
            )
            SELECT COALESCE(sf.name, 'Sin subfamilia') AS subfamily_name,
                   l.name AS location_name,
                   COALESCE(SUM(le.qty_in), 0) AS pieces_total,
                   COUNT(DISTINCT wo.id) AS orders_total
            FROM latest_execution le
            JOIN route_step rs ON rs.id = le.route_step_id
            JOIN active_locations al ON al.location_id = rs.location_id
            JOIN location l ON l.id = rs.location_id
            JOIN wip_item wip ON wip.id = le.wip_item_id
            JOIN work_order wo ON wo.id = wip.wo_order_id
            JOIN product p ON p.id = wo.product_id
            LEFT JOIN subfamily sf ON sf.id = p.id_subfamily
            WHERE le.rn = 1
              AND wo.status IN ('OPEN', 'IN_PROGRESS')
            GROUP BY subfamily_name, location_name", cn))
        {
            snapshotCmd.Parameters.AddWithValue("@snapshotCutoff", endDate.Date.AddDays(1));

            using var snapshotRd = snapshotCmd.ExecuteReader();
            while (snapshotRd.Read())
            {
                var key = $"{snapshotRd.GetString("location_name")}|{snapshotRd.GetString("subfamily_name")}";
                snapshotMap[key] = (
                    Pieces: Convert.ToInt32(snapshotRd.GetInt64("pieces_total")),
                    Orders: Convert.ToInt32(snapshotRd.GetInt64("orders_total")));
            }
        }

        foreach (var location in locationNames)
        {
            var row = new DiscreteInventoryLocationRowVm
            {
                Location = location
            };

            foreach (var subfamily in subfamilyNames)
            {
                var key = $"{location}|{subfamily}";
                var values = snapshotMap.TryGetValue(key, out var found) ? found : (Pieces: 0, Orders: 0);

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
                           COALESCE(LAG(wse.qty_in) OVER (PARTITION BY wse.wip_item_id ORDER BY wse.create_at, wse.id), wse.qty_in) - wse.qty_in,
                           0
                       ) AS calc_scrap
                FROM wip_step_execution wse
            )
            SELECT p.part_number,
                   COALESCE(SUM(sm.qty_in - sm.calc_scrap), 0) AS qty_produced,
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

    private static void ResolvePeriod(string periodType, string? weekValue, string? monthValue, DateTime? fromDate, DateTime? toDate, out DateTime start, out DateTime end, out string normalizedPeriodType, out string normalizedWeek, out string normalizedMonth)
    {
        var today = DateTime.UtcNow.Date;
        normalizedPeriodType = periodType;

        switch (periodType)
        {
            case "day" when fromDate.HasValue:
                start = fromDate.Value.Date;
                end = start;
                break;
            case "month" when !string.IsNullOrWhiteSpace(monthValue) && DateTime.TryParse($"{monthValue}-01", out var monthDate):
                start = new DateTime(monthDate.Year, monthDate.Month, 1);
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
            case "week" when !string.IsNullOrWhiteSpace(weekValue) && TryParseWeekValue(weekValue, out var weekStart):
                start = weekStart;
                end = weekStart.AddDays(6);
                break;
            default:
                start = GetStartOfWeek(today);
                end = start.AddDays(6);
                normalizedPeriodType = "week";
                break;
        }

        normalizedWeek = $"{ISOWeek.GetYear(start)}-W{ISOWeek.GetWeekOfYear(start):00}";
        normalizedMonth = $"{start:yyyy-MM}";
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
