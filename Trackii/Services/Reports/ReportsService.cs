using MySql.Data.MySqlClient;
using Trackii.Models.Reports;

namespace Trackii.Services.Reports;

public class ReportsService
{
    private readonly string _conn;

    public ReportsService(IConfiguration cfg)
    {
        _conn = cfg.GetConnectionString("TrackiiDb")
            ?? throw new Exception("Connection string TrackiiDb no configurada");
    }

    public WorkOrdersReportVm GetWorkOrders(string? search, string? status, int page, int pageSize)
    {
        var vm = new WorkOrdersReportVm
        {
            Search = search,
            Status = status,
            Page = page
        };

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        var where = " WHERE 1=1 ";
        if (!string.IsNullOrWhiteSpace(status)) where += " AND wo.status = @status ";
        if (!string.IsNullOrWhiteSpace(search)) where += " AND (wo.wo_number LIKE @search OR p.part_number LIKE @search) ";

        using var countCmd = new MySqlCommand($@"
            SELECT COUNT(*)
            FROM work_order wo
            JOIN product p ON p.id = wo.product_id
            LEFT JOIN wip_item wip ON wip.wo_order_id = wo.id
            LEFT JOIN route_step rs ON rs.id = wip.current_step_id
            LEFT JOIN location l ON l.id = rs.location_id
            {where}", cn);

        if (!string.IsNullOrWhiteSpace(status))
            countCmd.Parameters.AddWithValue("@status", status);
        if (!string.IsNullOrWhiteSpace(search))
            countCmd.Parameters.AddWithValue("@search", $"%{search}%");

        var total = Convert.ToInt32(countCmd.ExecuteScalar());
        vm.TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));

        using var cmd = new MySqlCommand($@"
            SELECT wo.wo_number,
                   wo.status,
                   p.part_number,
                   wip.created_at,
                   l.name AS location_name
            FROM work_order wo
            JOIN product p ON p.id = wo.product_id
            LEFT JOIN wip_item wip ON wip.wo_order_id = wo.id
            LEFT JOIN route_step rs ON rs.id = wip.current_step_id
            LEFT JOIN location l ON l.id = rs.location_id
            {where}
            ORDER BY wo.wo_number
            LIMIT @off,@lim", cn);

        if (!string.IsNullOrWhiteSpace(status))
            cmd.Parameters.AddWithValue("@status", status);
        if (!string.IsNullOrWhiteSpace(search))
            cmd.Parameters.AddWithValue("@search", $"%{search}%");

        cmd.Parameters.AddWithValue("@off", (page - 1) * pageSize);
        cmd.Parameters.AddWithValue("@lim", pageSize);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var locationOrdinal = rd.GetOrdinal("location_name");
            var createdAtOrdinal = rd.GetOrdinal("created_at");

            vm.Items.Add(new WorkOrdersReportVm.Row
            {
                WoNumber = rd.GetString("wo_number"),
                Status = rd.GetString("status"),
                Product = rd.GetString("part_number"),
                CurrentLocation = rd.IsDBNull(locationOrdinal) ? null : rd.GetString(locationOrdinal),
                WipCreatedAt = rd.IsDBNull(createdAtOrdinal) ? null : rd.GetDateTime(createdAtOrdinal)
            });
        }

        return vm;
    }

    public ProductsReportVm GetProducts(string? search, bool showInactive, int page, int pageSize)
    {
        var vm = new ProductsReportVm
        {
            Search = search,
            ShowInactive = showInactive,
            Page = page
        };

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        var where = " WHERE 1=1 ";
        if (!showInactive) where += " AND p.active = 1 ";
        if (!string.IsNullOrWhiteSpace(search)) where += " AND p.part_number LIKE @search ";

        using var countCmd = new MySqlCommand($@"
            SELECT COUNT(*)
            FROM product p
            {where}", cn);

        if (!string.IsNullOrWhiteSpace(search))
            countCmd.Parameters.AddWithValue("@search", $"%{search}%");

        var total = Convert.ToInt32(countCmd.ExecuteScalar());
        vm.TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));

        using var cmd = new MySqlCommand($@"
            SELECT p.part_number,
                   p.active,
                   a.name AS area_name,
                   f.name AS family_name,
                   s.name AS subfamily_name
            FROM product p
            JOIN subfamily s ON s.id = p.id_subfamily
            JOIN family f ON f.id = s.id_family
            JOIN area a ON a.id = f.id_area
            {where}
            ORDER BY p.part_number
            LIMIT @off,@lim", cn);

        if (!string.IsNullOrWhiteSpace(search))
            cmd.Parameters.AddWithValue("@search", $"%{search}%");

        cmd.Parameters.AddWithValue("@off", (page - 1) * pageSize);
        cmd.Parameters.AddWithValue("@lim", pageSize);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            vm.Items.Add(new ProductsReportVm.Row
            {
                PartNumber = rd.GetString("part_number"),
                Area = rd.GetString("area_name"),
                Family = rd.GetString("family_name"),
                Subfamily = rd.GetString("subfamily_name"),
                Active = rd.GetBoolean("active")
            });
        }

        return vm;
    }

    public ReworkReportVm GetRework(string? search, DateTime? from, DateTime? to, int page, int pageSize)
    {
        var vm = new ReworkReportVm
        {
            Search = search,
            From = from,
            To = to,
            Page = page
        };

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        var where = " WHERE 1=1 ";
        if (!string.IsNullOrWhiteSpace(search))
            where += " AND (wo.wo_number LIKE @search OR wrl.wip_item_id LIKE @search) ";
        if (from.HasValue) where += " AND wrl.created_at >= @from ";
        if (to.HasValue) where += " AND wrl.created_at <= @to ";

        using var countCmd = new MySqlCommand($@"
            SELECT COUNT(*)
            FROM wip_rework_log wrl
            JOIN wip_item wip ON wip.id = wrl.wip_item_id
            JOIN work_order wo ON wo.id = wip.wo_order_id
            {where}", cn);

        if (!string.IsNullOrWhiteSpace(search))
            countCmd.Parameters.AddWithValue("@search", $"%{search}%");
        if (from.HasValue) countCmd.Parameters.AddWithValue("@from", from.Value);
        if (to.HasValue) countCmd.Parameters.AddWithValue("@to", to.Value);

        var total = Convert.ToInt32(countCmd.ExecuteScalar());
        vm.TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));

        using var cmd = new MySqlCommand($@"
            SELECT wrl.created_at,
                   wrl.wip_item_id,
                   wrl.qty,
                   wrl.reason,
                   wo.wo_number,
                   l.name AS location_name,
                   u.username,
                   d.device_uid
            FROM wip_rework_log wrl
            JOIN wip_item wip ON wip.id = wrl.wip_item_id
            JOIN work_order wo ON wo.id = wip.wo_order_id
            JOIN location l ON l.id = wrl.location_id
            JOIN user u ON u.id = wrl.user_id
            JOIN devices d ON d.id = wrl.device_id
            {where}
            ORDER BY wrl.created_at DESC
            LIMIT @off,@lim", cn);

        if (!string.IsNullOrWhiteSpace(search))
            cmd.Parameters.AddWithValue("@search", $"%{search}%");
        if (from.HasValue) cmd.Parameters.AddWithValue("@from", from.Value);
        if (to.HasValue) cmd.Parameters.AddWithValue("@to", to.Value);

        cmd.Parameters.AddWithValue("@off", (page - 1) * pageSize);
        cmd.Parameters.AddWithValue("@lim", pageSize);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            vm.Items.Add(new ReworkReportVm.Row
            {
                CreatedAt = rd.GetDateTime("created_at"),
                WoNumber = rd.GetString("wo_number"),
                WipItemId = rd.GetUInt32("wip_item_id"),
                Location = rd.GetString("location_name"),
                User = rd.GetString("username"),
                Device = rd.GetString("device_uid"),
                Qty = Convert.ToInt32(rd.GetUInt32("qty")),
                Reason = rd.IsDBNull(rd.GetOrdinal("reason")) ? null : rd.GetString("reason")
            });
        }

        return vm;
    }

    public ProductsByAreaReportVm GetProductsByArea(int page, int pageSize)
    {
        var vm = new ProductsByAreaReportVm
        {
            Page = page
        };

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var countCmd = new MySqlCommand("SELECT COUNT(*) FROM area", cn);
        var total = Convert.ToInt32(countCmd.ExecuteScalar());
        vm.TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));

        using var cmd = new MySqlCommand(@"
            SELECT a.name AS area_name,
                   COUNT(p.id) AS total_products,
                   SUM(CASE WHEN p.active = 1 THEN 1 ELSE 0 END) AS active_products
            FROM area a
            LEFT JOIN family f ON f.id_area = a.id
            LEFT JOIN subfamily s ON s.id_family = f.id
            LEFT JOIN product p ON p.id_subfamily = s.id
            GROUP BY a.id, a.name
            ORDER BY a.name
            LIMIT @off,@lim", cn);

        cmd.Parameters.AddWithValue("@off", (page - 1) * pageSize);
        cmd.Parameters.AddWithValue("@lim", pageSize);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            vm.Items.Add(new ProductsByAreaReportVm.Row
            {
                Area = rd.GetString("area_name"),
                Products = Convert.ToInt32(rd.GetInt64("total_products")),
                ActiveProducts = Convert.ToInt32(rd.GetInt64("active_products"))
            });
        }

        return vm;
    }

    public WipReportVm GetWipItems(string? status, int page, int pageSize)
    {
        var vm = new WipReportVm
        {
            Status = status,
            Page = page
        };

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        var where = " WHERE 1=1 ";
        if (!string.IsNullOrWhiteSpace(status)) where += " AND wip.status = @status ";

        using var countCmd = new MySqlCommand($@"
            SELECT COUNT(*)
            FROM wip_item wip
            {where}", cn);

        if (!string.IsNullOrWhiteSpace(status))
            countCmd.Parameters.AddWithValue("@status", status);

        var total = Convert.ToInt32(countCmd.ExecuteScalar());
        vm.TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));

        using var cmd = new MySqlCommand($@"
            SELECT wip.id,
                   wip.status,
                   wip.created_at,
                   wo.wo_number,
                   l.name AS location_name
            FROM wip_item wip
            JOIN work_order wo ON wo.id = wip.wo_order_id
            LEFT JOIN route_step rs ON rs.id = wip.current_step_id
            LEFT JOIN location l ON l.id = rs.location_id
            {where}
            ORDER BY wip.created_at DESC
            LIMIT @off,@lim", cn);

        if (!string.IsNullOrWhiteSpace(status))
            cmd.Parameters.AddWithValue("@status", status);

        cmd.Parameters.AddWithValue("@off", (page - 1) * pageSize);
        cmd.Parameters.AddWithValue("@lim", pageSize);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var locationOrdinal = rd.GetOrdinal("location_name");
            vm.Items.Add(new WipReportVm.Row
            {
                WipItemId = rd.GetUInt32("id"),
                Status = rd.GetString("status"),
                WoNumber = rd.GetString("wo_number"),
                Location = rd.IsDBNull(locationOrdinal) ? null : rd.GetString(locationOrdinal),
                CreatedAt = rd.GetDateTime("created_at")
            });
        }

        return vm;
    }

    public DevicesReportVm GetDevices(string? search, bool onlyActive, int page, int pageSize)
    {
        var vm = new DevicesReportVm
        {
            Search = search,
            OnlyActive = onlyActive,
            Page = page
        };

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        var where = " WHERE 1=1 ";
        if (onlyActive) where += " AND d.active = 1 ";
        if (!string.IsNullOrWhiteSpace(search)) where += " AND (d.device_uid LIKE @search OR d.name LIKE @search) ";

        using var countCmd = new MySqlCommand($@"
            SELECT COUNT(*)
            FROM devices d
            {where}", cn);

        if (!string.IsNullOrWhiteSpace(search))
            countCmd.Parameters.AddWithValue("@search", $"%{search}%");

        var total = Convert.ToInt32(countCmd.ExecuteScalar());
        vm.TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));

        using var cmd = new MySqlCommand($@"
            SELECT d.device_uid,
                   d.name,
                   d.active,
                   l.name AS location_name
            FROM devices d
            JOIN location l ON l.id = d.location_id
            {where}
            ORDER BY d.device_uid
            LIMIT @off,@lim", cn);

        if (!string.IsNullOrWhiteSpace(search))
            cmd.Parameters.AddWithValue("@search", $"%{search}%");

        cmd.Parameters.AddWithValue("@off", (page - 1) * pageSize);
        cmd.Parameters.AddWithValue("@lim", pageSize);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            vm.Items.Add(new DevicesReportVm.Row
            {
                DeviceUid = rd.GetString("device_uid"),
                Name = rd.IsDBNull(rd.GetOrdinal("name")) ? null : rd.GetString("name"),
                Location = rd.GetString("location_name"),
                Active = rd.GetBoolean("active")
            });
        }

        return vm;
    }

    public UsersReportVm GetUsers(string? search, bool onlyActive, int page, int pageSize)
    {
        var vm = new UsersReportVm
        {
            Search = search,
            OnlyActive = onlyActive,
            Page = page
        };

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        var where = " WHERE 1=1 ";
        if (onlyActive) where += " AND u.active = 1 ";
        if (!string.IsNullOrWhiteSpace(search)) where += " AND u.username LIKE @search ";

        using var countCmd = new MySqlCommand($@"
            SELECT COUNT(*)
            FROM user u
            {where}", cn);

        if (!string.IsNullOrWhiteSpace(search))
            countCmd.Parameters.AddWithValue("@search", $"%{search}%");

        var total = Convert.ToInt32(countCmd.ExecuteScalar());
        vm.TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));

        using var cmd = new MySqlCommand($@"
            SELECT u.username,
                   u.active,
                   r.name AS role_name
            FROM user u
            JOIN role r ON r.id = u.role_id
            {where}
            ORDER BY u.username
            LIMIT @off,@lim", cn);

        if (!string.IsNullOrWhiteSpace(search))
            cmd.Parameters.AddWithValue("@search", $"%{search}%");

        cmd.Parameters.AddWithValue("@off", (page - 1) * pageSize);
        cmd.Parameters.AddWithValue("@lim", pageSize);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            vm.Items.Add(new UsersReportVm.Row
            {
                Username = rd.GetString("username"),
                Role = rd.GetString("role_name"),
                Active = rd.GetBoolean("active")
            });
        }

        return vm;
    }
}
