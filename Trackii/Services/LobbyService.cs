using MySql.Data.MySqlClient;
using Trackii.Models.Engineering;
using Trackii.Models.Lobby;

namespace Trackii.Services;

public class LobbyService
{
    private readonly string _conn;

    public LobbyService(IConfiguration cfg)
    {
        _conn = cfg.GetConnectionString("TrackiiDb")
            ?? throw new Exception("Connection string TrackiiDb no configurada");
    }

    public LobbyVm GetDashboard()
    {
        var vm = new LobbyVm();

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        vm.AreasCount = CountTable(cn, "area");
        vm.FamiliesCount = CountTable(cn, "family");
        vm.SubfamiliesCount = CountTable(cn, "subfamily");
        vm.ProductsCount = CountTable(cn, "product");
        vm.RoutesCount = CountTable(cn, "route");
        vm.LocationsCount = CountTable(cn, "location");
        vm.UsersCount = CountTable(cn, "user");
        vm.RolesCount = CountTable(cn, "role");

        LoadAreaProductChart(cn, vm.AreaProductChart);
        LoadProductStatusChart(cn, vm.ProductStatusChart);
        LoadWorkOrderStatusChart(cn, vm.WorkOrderStatusChart);
        LoadWipStatusChart(cn, vm.WipStatusChart);
        LoadUsersByRoleChart(cn, vm.UsersByRoleChart);

        return vm;
    }

    public AdminLobbyVm GetAdminDashboard()
    {
        var vm = new AdminLobbyVm();

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        vm.AreasCount = CountTable(cn, "area");
        vm.FamiliesCount = CountTable(cn, "family");
        vm.SubfamiliesCount = CountTable(cn, "subfamily");
        vm.ProductsCount = CountTable(cn, "product");
        vm.RoutesCount = CountTable(cn, "route");
        vm.LocationsCount = CountTable(cn, "location");
        vm.UsersCount = CountTable(cn, "user");
        vm.RolesCount = CountTable(cn, "role");

        LoadActiveDevices(cn, vm);
        LoadActiveUsers(cn, vm);

        return vm;
    }

    public EngineeringLobbyVm GetEngineeringDashboard()
    {
        var vm = new EngineeringLobbyVm();

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var countCmd = new MySqlCommand(@"
            SELECT
                SUM(CASE WHEN active = 1 THEN 1 ELSE 0 END) AS active_count,
                COUNT(*) AS total_count
            FROM unregistered_parts", cn);

        using (var rd = countCmd.ExecuteReader())
        {
            if (rd.Read())
            {
                vm.ActiveUnregisteredCount = Convert.ToInt32(rd.GetInt64("active_count"));
                vm.TotalUnregisteredCount = Convert.ToInt32(rd.GetInt64("total_count"));
            }
        }

        using var oldestCmd = new MySqlCommand(@"
            SELECT part_id, part_number, creation_datetime,
                   DATEDIFF(NOW(), creation_datetime) AS age_days
            FROM unregistered_parts
            WHERE active = 1
            ORDER BY creation_datetime
            LIMIT 5", cn);

        using (var rd = oldestCmd.ExecuteReader())
        {
            while (rd.Read())
            {
                vm.OldestUnregistered.Add(new EngineeringLobbyVm.UnregisteredPartRow
                {
                    PartId = rd.GetUInt32("part_id"),
                    PartNumber = rd.GetString("part_number"),
                    CreatedAt = rd.GetDateTime("creation_datetime"),
                    AgeDays = Convert.ToInt32(rd.GetInt64("age_days"))
                });
            }
        }

        using var recentCmd = new MySqlCommand(@"
            SELECT part_id, part_number, creation_datetime,
                   DATEDIFF(NOW(), creation_datetime) AS age_days
            FROM unregistered_parts
            WHERE active = 1
            ORDER BY creation_datetime DESC
            LIMIT 5", cn);

        using (var recentRd = recentCmd.ExecuteReader())
        {
            while (recentRd.Read())
            {
                vm.RecentUnregistered.Add(new EngineeringLobbyVm.UnregisteredPartRow
                {
                    PartId = recentRd.GetUInt32("part_id"),
                    PartNumber = recentRd.GetString("part_number"),
                    CreatedAt = recentRd.GetDateTime("creation_datetime"),
                    AgeDays = Convert.ToInt32(recentRd.GetInt64("age_days"))
                });
            }
        }

        var activeOrders = LoadEngineeringActiveOrders(cn);
        vm.ActiveWorkOrdersCount = activeOrders.Count;
        vm.OpenWorkOrdersCount = activeOrders.Count(x => x.Status == "OPEN");
        vm.InProgressWorkOrdersCount = activeOrders.Count(x => x.Status == "IN_PROGRESS");
        vm.ActiveLocationsCount = activeOrders
            .Where(x => x.LocationId.HasValue)
            .Select(x => x.LocationId!.Value)
            .Distinct()
            .Count();

        foreach (var item in activeOrders
                     .OrderByDescending(x => x.AgeDays ?? -1)
                     .ThenBy(x => x.WoNumber)
                     .Take(8))
        {
            vm.PriorityWorkOrders.Add(new EngineeringLobbyVm.ActiveWorkOrderRow
            {
                WorkOrderId = item.WorkOrderId,
                WoNumber = item.WoNumber,
                Status = item.Status,
                Product = item.Product,
                Family = item.Family,
                Subfamily = item.Subfamily,
                Route = item.Route,
                Location = item.Location,
                WipCreatedAt = item.WipCreatedAt,
                AgeDays = item.AgeDays
            });
        }

        foreach (var item in activeOrders
                     .GroupBy(x => x.Family)
                     .Select(g => new EngineeringLobbyVm.BreakdownRow { Label = g.Key, Count = g.Count() })
                     .OrderByDescending(x => x.Count)
                     .ThenBy(x => x.Label)
                     .Take(5))
        {
            vm.FamilyBreakdown.Add(item);
        }

        foreach (var item in activeOrders
                     .GroupBy(x => x.Location)
                     .Select(g => new EngineeringLobbyVm.BreakdownRow { Label = g.Key, Count = g.Count() })
                     .OrderByDescending(x => x.Count)
                     .ThenBy(x => x.Label)
                     .Take(5))
        {
            vm.LocationBreakdown.Add(item);
        }

        return vm;
    }

    public EngineeringActiveOrdersVm GetEngineeringActiveOrders(string? search, string? status, uint? familyId, uint? subfamilyId, uint? focusSubfamilyId, uint? locationId, uint? routeId)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        var allItems = LoadEngineeringActiveOrders(cn);
        var vm = new EngineeringActiveOrdersVm
        {
            Search = search?.Trim(),
            Status = status,
            FamilyId = familyId,
            SubfamilyId = subfamilyId,
            FocusSubfamilyId = focusSubfamilyId ?? subfamilyId,
            LocationId = locationId,
            RouteId = routeId,
            ActiveOrdersCount = allItems.Count,
            DistinctFamiliesCount = allItems.Select(x => x.FamilyId).Distinct().Count(),
            DistinctSubfamiliesCount = allItems.Select(x => x.SubfamilyId).Distinct().Count(),
            DistinctLocationsCount = allItems.Where(x => x.LocationId.HasValue).Select(x => x.LocationId!.Value).Distinct().Count(),
            DistinctRoutesCount = allItems.Where(x => x.RouteId.HasValue).Select(x => x.RouteId!.Value).Distinct().Count()
        };

        foreach (var item in allItems
                     .GroupBy(x => new { x.FamilyId, x.Family })
                     .Select(g => new EngineeringActiveOrdersVm.FilterOption
                     {
                         Id = g.Key.FamilyId,
                         Name = g.Key.Family,
                         Count = g.Count()
                     })
                     .OrderBy(x => x.Name))
        {
            vm.Families.Add(item);
        }

        foreach (var item in allItems
                     .GroupBy(x => new { x.SubfamilyId, x.Subfamily, x.FamilyId })
                     .Select(g => new EngineeringActiveOrdersVm.SubfamilyOption
                     {
                         Id = g.Key.SubfamilyId,
                         FamilyId = g.Key.FamilyId,
                         Name = g.Key.Subfamily,
                         Count = g.Count()
                     })
                     .OrderBy(x => x.Name))
        {
            vm.Subfamilies.Add(item);
        }

        foreach (var item in allItems
                     .Where(x => x.LocationId.HasValue)
                     .GroupBy(x => new { Id = x.LocationId!.Value, x.Location })
                     .Select(g => new EngineeringActiveOrdersVm.FilterOption
                     {
                         Id = g.Key.Id,
                         Name = g.Key.Location,
                         Count = g.Count()
                     })
                     .OrderBy(x => x.Name))
        {
            vm.Locations.Add(item);
        }

        foreach (var item in allItems
                     .Where(x => x.RouteId.HasValue)
                     .GroupBy(x => new { Id = x.RouteId!.Value, x.Route })
                     .Select(g => new EngineeringActiveOrdersVm.FilterOption
                     {
                         Id = g.Key.Id,
                         Name = g.Key.Route,
                         Count = g.Count()
                     })
                     .OrderBy(x => x.Name))
        {
            vm.Routes.Add(item);
        }

        IEnumerable<EngineeringActiveOrdersVm.OrderRow> filtered = allItems;

        if (!string.IsNullOrWhiteSpace(vm.Search))
        {
            filtered = filtered.Where(x =>
                x.WoNumber.Contains(vm.Search, StringComparison.OrdinalIgnoreCase) ||
                x.Product.Contains(vm.Search, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(vm.Status))
            filtered = filtered.Where(x => string.Equals(x.Status, vm.Status, StringComparison.OrdinalIgnoreCase));
        if (vm.FamilyId.HasValue)
            filtered = filtered.Where(x => x.FamilyId == vm.FamilyId.Value);
        if (vm.SubfamilyId.HasValue)
            filtered = filtered.Where(x => x.SubfamilyId == vm.SubfamilyId.Value);
        if (vm.LocationId.HasValue)
            filtered = filtered.Where(x => x.LocationId == vm.LocationId.Value);
        if (vm.RouteId.HasValue)
            filtered = filtered.Where(x => x.RouteId == vm.RouteId.Value);

        var filteredList = filtered
            .OrderByDescending(x => x.AgeDays ?? -1)
            .ThenBy(x => x.WoNumber)
            .ToList();

        foreach (var item in filteredList)
        {
            vm.Items.Add(item);
        }

        if (vm.FocusSubfamilyId.HasValue)
        {
            foreach (var item in allItems
                         .Where(x => x.SubfamilyId == vm.FocusSubfamilyId.Value)
                         .OrderBy(x => x.NextLocation)
                         .ThenBy(x => x.Location)
                         .ThenBy(x => x.WoNumber))
            {
                vm.FocusSubfamilyItems.Add(item);
            }
        }

        PopulateBreakdown(vm.FamilySummary, filteredList.GroupBy(x => x.Family));
        PopulateBreakdown(vm.SubfamilySummary, filteredList.GroupBy(x => x.Subfamily));
        PopulateBreakdown(vm.LocationSummary, filteredList.GroupBy(x => x.Location));
        PopulateBreakdown(vm.RouteSummary, filteredList.GroupBy(x => x.Route));

        return vm;
    }

    private static int CountTable(MySqlConnection cn, string table)
    {
        using var cmd = new MySqlCommand($"SELECT COUNT(*) FROM `{table}`", cn);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void PopulateBreakdown(
        List<EngineeringActiveOrdersVm.BreakdownRow> target,
        IEnumerable<IGrouping<string, EngineeringActiveOrdersVm.OrderRow>> groups)
    {
        foreach (var item in groups
                     .Select(g => new EngineeringActiveOrdersVm.BreakdownRow
                     {
                         Label = g.Key,
                         Count = g.Count()
                     })
                     .OrderByDescending(x => x.Count)
                     .ThenBy(x => x.Label)
                     .Take(8))
        {
            target.Add(item);
        }
    }

    private static List<EngineeringActiveOrdersVm.OrderRow> LoadEngineeringActiveOrders(MySqlConnection cn)
    {
        var items = new List<EngineeringActiveOrdersVm.OrderRow>();

        using var cmd = new MySqlCommand(@"
            SELECT wo.id AS work_order_id,
                   wo.wo_number,
                   wo.status,
                   p.part_number,
                   f.id AS family_id,
                   f.name AS family_name,
                   s.id AS subfamily_id,
                   s.name AS subfamily_name,
                   COALESCE(wip.route_id, s.active_route_id) AS route_id,
                   COALESCE(CONCAT(r.name, ' v', r.version), 'Sin ruta') AS route_name,
                   l.id AS location_id,
                   COALESCE(l.name, 'Sin localidad') AS location_name,
                   CASE
                       WHEN rs.id IS NULL THEN 'Sin paso actual'
                       ELSE CONCAT('Paso ', rs.step_number, ' · ', COALESCE(l.name, 'Sin localidad'))
                   END AS current_step_label,
                   wse.qty_in AS current_step_qty,
                   CASE
                       WHEN next_rs.id IS NULL THEN (CASE WHEN COALESCE(wip.route_id, s.active_route_id) IS NULL THEN 'Sin ruta' ELSE 'Fin de ruta' END)
                       ELSE CONCAT('Paso ', next_rs.step_number, ' · ', COALESCE(next_loc.name, 'Sin localidad'))
                   END AS next_step_label,
                   next_loc.name AS next_location_name,
                   wip.created_at,
                   DATEDIFF(NOW(), wip.created_at) AS age_days
            FROM work_order wo
            JOIN product p ON p.id = wo.product_id
            JOIN subfamily s ON s.id = p.id_subfamily
            JOIN family f ON f.id = s.id_family
            LEFT JOIN wip_item wip ON wip.wo_order_id = wo.id
            LEFT JOIN route r ON r.id = COALESCE(wip.route_id, s.active_route_id)
            LEFT JOIN route_step rs ON rs.id = wip.current_step_id
            LEFT JOIN route_step next_rs ON next_rs.route_id = COALESCE(wip.route_id, s.active_route_id)
                                         AND next_rs.step_number = CASE
                                             WHEN rs.id IS NULL THEN 1
                                             ELSE rs.step_number + 1
                                         END
            LEFT JOIN location l ON l.id = rs.location_id
            LEFT JOIN location next_loc ON next_loc.id = next_rs.location_id
            LEFT JOIN wip_step_execution wse ON wse.id = (
                SELECT wse2.id
                FROM wip_step_execution wse2
                WHERE wse2.wip_item_id = wip.id
                  AND (wip.current_step_id IS NULL OR wse2.route_step_id = wip.current_step_id)
                ORDER BY wse2.create_at DESC, wse2.id DESC
                LIMIT 1
            )
            WHERE wo.status IN ('OPEN', 'IN_PROGRESS')
            ORDER BY wo.wo_number", cn);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var routeIdOrdinal = rd.GetOrdinal("route_id");
            var locationIdOrdinal = rd.GetOrdinal("location_id");
            var createdAtOrdinal = rd.GetOrdinal("created_at");
            var ageDaysOrdinal = rd.GetOrdinal("age_days");

            items.Add(new EngineeringActiveOrdersVm.OrderRow
            {
                WorkOrderId = rd.GetUInt32("work_order_id"),
                WoNumber = rd.GetString("wo_number"),
                Status = rd.GetString("status"),
                Product = rd.GetString("part_number"),
                FamilyId = rd.GetUInt32("family_id"),
                Family = rd.GetString("family_name"),
                SubfamilyId = rd.GetUInt32("subfamily_id"),
                Subfamily = rd.GetString("subfamily_name"),
                RouteId = rd.IsDBNull(routeIdOrdinal) ? null : rd.GetUInt32(routeIdOrdinal),
                Route = rd.GetString("route_name"),
                LocationId = rd.IsDBNull(locationIdOrdinal) ? null : rd.GetUInt32(locationIdOrdinal),
                Location = rd.GetString("location_name"),
                CurrentStep = rd.GetString("current_step_label"),
                CurrentStepQty = rd.IsDBNull(rd.GetOrdinal("current_step_qty")) ? null : Convert.ToInt32(rd.GetUInt32("current_step_qty")),
                NextStep = rd.GetString("next_step_label"),
                NextLocation = rd.IsDBNull(rd.GetOrdinal("next_location_name"))
                    ? (rd.IsDBNull(routeIdOrdinal) ? "Sin ruta" : "Fin de ruta")
                    : rd.GetString("next_location_name"),
                WipCreatedAt = rd.IsDBNull(createdAtOrdinal) ? null : rd.GetDateTime(createdAtOrdinal),
                AgeDays = rd.IsDBNull(ageDaysOrdinal) ? null : Convert.ToInt32(rd.GetInt64(ageDaysOrdinal))
            });
        }

        return items;
    }

    private static void LoadActiveDevices(MySqlConnection cn, AdminLobbyVm vm)
    {
        using var cmd = new MySqlCommand(@"
            SELECT d.device_uid, COALESCE(d.name, '') AS device_name, l.name AS location_name
            FROM devices d
            JOIN location l ON l.id = d.location_id
            WHERE d.active = 1
            ORDER BY l.name, d.device_uid", cn);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            vm.ActiveDevices.Add(new AdminLobbyVm.ActiveDeviceVm
            {
                DeviceUid = rd.GetString("device_uid"),
                Name = rd.GetString("device_name"),
                Location = rd.GetString("location_name")
            });
        }

        vm.ActiveDevicesCount = vm.ActiveDevices.Count;
    }

    private static void LoadActiveUsers(MySqlConnection cn, AdminLobbyVm vm)
    {
        using var cmd = new MySqlCommand(@"
            SELECT u.username, r.name AS role_name
            FROM user u
            JOIN role r ON r.id = u.role_id
            WHERE u.active = 1
            ORDER BY u.username", cn);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            vm.ActiveUsers.Add(new AdminLobbyVm.ActiveUserVm
            {
                Username = rd.GetString("username"),
                Role = rd.GetString("role_name")
            });
        }
    }

    private static void LoadAreaProductChart(MySqlConnection cn, LobbyVm.ChartVm chart)
    {
        using var cmd = new MySqlCommand(@"
            SELECT a.name, COUNT(p.id) AS total
            FROM area a
            LEFT JOIN family f ON f.id_area = a.id
            LEFT JOIN subfamily s ON s.id_family = f.id
            LEFT JOIN product p ON p.id_subfamily = s.id
            GROUP BY a.id, a.name
            ORDER BY a.name", cn);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            chart.Labels.Add(rd.GetString("name"));
            chart.Values.Add(Convert.ToInt32(rd.GetInt64("total")));
        }
    }

    private static void LoadProductStatusChart(MySqlConnection cn, LobbyVm.ChartVm chart)
    {
        using var cmd = new MySqlCommand(@"
            SELECT
                SUM(CASE WHEN active = 1 THEN 1 ELSE 0 END) AS active_count,
                SUM(CASE WHEN active = 0 THEN 1 ELSE 0 END) AS inactive_count
            FROM product", cn);

        using var rd = cmd.ExecuteReader();
        if (rd.Read())
        {
            chart.Labels.Add("Activos");
            chart.Values.Add(Convert.ToInt32(rd.GetInt64("active_count")));
            chart.Labels.Add("Inactivos");
            chart.Values.Add(Convert.ToInt32(rd.GetInt64("inactive_count")));
        }
    }

    private static void LoadWorkOrderStatusChart(MySqlConnection cn, LobbyVm.ChartVm chart)
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

    private static void LoadWipStatusChart(MySqlConnection cn, LobbyVm.ChartVm chart)
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

    private static void LoadUsersByRoleChart(MySqlConnection cn, LobbyVm.ChartVm chart)
    {
        using var cmd = new MySqlCommand(@"
            SELECT r.name, COUNT(u.id) AS total
            FROM role r
            LEFT JOIN user u ON u.role_id = r.id
            GROUP BY r.id, r.name
            ORDER BY r.name", cn);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            chart.Labels.Add(rd.GetString("name"));
            chart.Values.Add(Convert.ToInt32(rd.GetInt64("total")));
        }
    }
}
