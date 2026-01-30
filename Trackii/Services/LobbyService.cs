using MySql.Data.MySqlClient;
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

    private static int CountTable(MySqlConnection cn, string table)
    {
        using var cmd = new MySqlCommand($"SELECT COUNT(*) FROM `{table}`", cn);
        return Convert.ToInt32(cmd.ExecuteScalar());
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
