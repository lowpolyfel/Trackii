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

        using var recentRd = recentCmd.ExecuteReader();
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

        return vm;
    }

    private static int CountTable(MySqlConnection cn, string table)
    {
        using var cmd = new MySqlCommand($"SELECT COUNT(*) FROM `{table}`", cn);
        return Convert.ToInt32(cmd.ExecuteScalar());
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
