using MySql.Data.MySqlClient;
using Trackii.Models.Admin.Product;

namespace Trackii.Services.Admin;

public class ProductService
{
    private readonly string _conn;

    public ProductService(IConfiguration cfg)
    {
        _conn = cfg.GetConnectionString("TrackiiDb")
            ?? throw new Exception("Connection string TrackiiDb no configurada");
    }

    // ===================== LISTADO =====================
    public ProductListVm GetPaged(
        uint? areaId,
        uint? familyId,
        uint? subfamilyId,
        string? search,
        bool showInactive,
        int page,
        int pageSize)
    {
        var vm = new ProductListVm
        {
            AreaId = areaId,
            FamilyId = familyId,
            SubfamilyId = subfamilyId,
            Search = search,
            ShowInactive = showInactive,
            Page = page
        };

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        vm.Areas = GetActiveAreas(cn);
        vm.Families = GetActiveFamilies(cn, areaId);
        vm.Subfamilies = GetActiveSubfamilies(cn, familyId);

        var where = " WHERE 1=1 ";
        if (!showInactive) where += " AND p.active = 1 ";
        if (areaId.HasValue) where += " AND a.id = @area ";
        if (familyId.HasValue) where += " AND f.id = @family ";
        if (subfamilyId.HasValue) where += " AND s.id = @subfamily ";
        if (!string.IsNullOrWhiteSpace(search)) where += " AND p.part_number LIKE @search ";

        // COUNT
        using (var countCmd = new MySqlCommand($@"
            SELECT COUNT(*)
            FROM product p
            JOIN subfamily s ON s.id = p.id_subfamily
            JOIN family f ON f.id = s.id_family
            JOIN area a ON a.id = f.id_area
            {where}", cn))
        {
            AddFilters(countCmd, areaId, familyId, subfamilyId, search);
            var total = Convert.ToInt32(countCmd.ExecuteScalar());
            vm.TotalPages = (int)Math.Ceiling(total / (double)pageSize);
        }

        // DATA
        using (var cmd = new MySqlCommand($@"
            SELECT p.id, p.part_number, p.active,
                   s.name AS subfamily,
                   f.name AS family,
                   a.name AS area
            FROM product p
            JOIN subfamily s ON s.id = p.id_subfamily
            JOIN family f ON f.id = s.id_family
            JOIN area a ON a.id = f.id_area
            {where}
            ORDER BY a.name, f.name, s.name, p.part_number
            LIMIT @off,@lim", cn))
        {
            AddFilters(cmd, areaId, familyId, subfamilyId, search);
            cmd.Parameters.AddWithValue("@off", (page - 1) * pageSize);
            cmd.Parameters.AddWithValue("@lim", pageSize);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                vm.Items.Add(new ProductListVm.Row
                {
                    Id = rd.GetUInt32("id"),
                    PartNumber = rd.GetString("part_number"),
                    Active = rd.GetBoolean("active"),
                    Subfamily = rd.GetString("subfamily"),
                    Family = rd.GetString("family"),
                    Area = rd.GetString("area")
                });
            }
        }

        return vm;
    }

    // ===================== CRUD =====================
    public ProductEditVm? GetById(uint id)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(
            "SELECT id, id_subfamily, part_number FROM product WHERE id=@id", cn);

        cmd.Parameters.AddWithValue("@id", id);
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;

        return new ProductEditVm
        {
            Id = id,
            SubfamilyId = rd.GetUInt32("id_subfamily"),
            PartNumber = rd.GetString("part_number")
        };
    }

    // ===================== CREATE (Con Validación) =====================
    public void Create(ProductEditVm vm)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        // VALIDACIÓN: Padre (Subfamilia) activa
        using (var check = new MySqlCommand("SELECT active FROM subfamily WHERE id=@sid", cn))
        {
            check.Parameters.AddWithValue("@sid", vm.SubfamilyId);
            var active = check.ExecuteScalar();
            if (active == null || !Convert.ToBoolean(active))
                throw new Exception("No se puede crear: La Subfamilia seleccionada está inactiva.");
        }

        using var cmd = new MySqlCommand(@"
            INSERT INTO product (id_subfamily, part_number, active)
            VALUES (@s,@p,1)", cn);

        cmd.Parameters.AddWithValue("@s", vm.SubfamilyId);
        cmd.Parameters.AddWithValue("@p", vm.PartNumber);
        cmd.ExecuteNonQuery();
    }

    // ===================== UPDATE (Con Validación) =====================
    public void Update(ProductEditVm vm)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        // VALIDACIÓN: Padre (Subfamilia) activa
        using (var check = new MySqlCommand("SELECT active FROM subfamily WHERE id=@sid", cn))
        {
            check.Parameters.AddWithValue("@sid", vm.SubfamilyId);
            var active = check.ExecuteScalar();
            if (active == null || !Convert.ToBoolean(active))
                throw new Exception("No se puede actualizar: La Subfamilia seleccionada está inactiva.");
        }

        using var cmd = new MySqlCommand(@"
            UPDATE product
            SET id_subfamily=@s, part_number=@p
            WHERE id=@id", cn);

        cmd.Parameters.AddWithValue("@id", vm.Id);
        cmd.Parameters.AddWithValue("@s", vm.SubfamilyId);
        cmd.Parameters.AddWithValue("@p", vm.PartNumber);
        cmd.ExecuteNonQuery();
    }

    // ===================== TOGGLE (Con Validación) =====================
    public bool SetActive(uint id, bool active)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        if (active)
        {
            // VALIDACIÓN: Padre (Subfamilia) activa antes de activar
            using var chk = new MySqlCommand(@"
                SELECT s.active
                FROM product p
                JOIN subfamily s ON s.id = p.id_subfamily
                WHERE p.id=@id", cn);

            chk.Parameters.AddWithValue("@id", id);
            var parentActive = chk.ExecuteScalar();
            if (parentActive == null || !Convert.ToBoolean(parentActive))
                return false; // Padre inactivo
        }
        else
        {
            // VALIDACIÓN: No desactivar si tiene WIP o WorkOrders
            using var dep = new MySqlCommand(@"
                SELECT
                  (SELECT COUNT(*) FROM work_order WHERE product_id=@id) +
                  (SELECT COUNT(*) FROM wip_item w
                     JOIN work_order wo ON wo.id = w.wo_order_id
                     WHERE wo.product_id=@id)", cn);

            dep.Parameters.AddWithValue("@id", id);
            if (Convert.ToInt32(dep.ExecuteScalar()) > 0)
                return false; // Está en uso
        }

        using var cmd = new MySqlCommand(
            "UPDATE product SET active=@a WHERE id=@id", cn);

        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@a", active);
        cmd.ExecuteNonQuery();
        return true;
    }

    // ===================== LOOKUPS =====================
    public List<(uint Id, string Name)> GetActiveSubfamilies()
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        var list = new List<(uint, string)>();
        using var cmd = new MySqlCommand(
            "SELECT id,name FROM subfamily WHERE active=1 ORDER BY name", cn);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            list.Add((rd.GetUInt32(0), rd.GetString(1)));

        return list;
    }

    private static List<(uint, string)> GetActiveAreas(MySqlConnection cn)
    {
        var list = new List<(uint, string)>();
        using var cmd = new MySqlCommand(
            "SELECT id,name FROM area WHERE active=1 ORDER BY name", cn);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            list.Add((rd.GetUInt32(0), rd.GetString(1)));

        rd.Close();
        return list;
    }

    private static List<(uint, string)> GetActiveFamilies(MySqlConnection cn, uint? areaId)
    {
        var list = new List<(uint, string)>();
        var sql = "SELECT id,name FROM family WHERE active=1";
        if (areaId.HasValue) sql += " AND id_area=@a";

        using var cmd = new MySqlCommand(sql, cn);
        if (areaId.HasValue)
            cmd.Parameters.AddWithValue("@a", areaId);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            list.Add((rd.GetUInt32(0), rd.GetString(1)));

        rd.Close();
        return list;
    }

    private static List<(uint, string)> GetActiveSubfamilies(MySqlConnection cn, uint? familyId)
    {
        var list = new List<(uint, string)>();
        var sql = "SELECT id,name FROM subfamily WHERE active=1";
        if (familyId.HasValue) sql += " AND id_family=@f";

        using var cmd = new MySqlCommand(sql, cn);
        if (familyId.HasValue)
            cmd.Parameters.AddWithValue("@f", familyId);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            list.Add((rd.GetUInt32(0), rd.GetString(1)));

        rd.Close();
        return list;
    }

    private static void AddFilters(
        MySqlCommand cmd,
        uint? areaId,
        uint? familyId,
        uint? subfamilyId,
        string? search)
    {
        if (areaId.HasValue) cmd.Parameters.AddWithValue("@area", areaId);
        if (familyId.HasValue) cmd.Parameters.AddWithValue("@family", familyId);
        if (subfamilyId.HasValue) cmd.Parameters.AddWithValue("@subfamily", subfamilyId);
        if (!string.IsNullOrWhiteSpace(search))
            cmd.Parameters.AddWithValue("@search", $"%{search}%");
    }

    // VALIDACIÓN DE DUPLICADOS
    public bool Exists(string partNumber, uint? id = null)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        var query = "SELECT COUNT(*) FROM product WHERE part_number = @p";
        if (id.HasValue)
            query += " AND id != @id";

        using var cmd = new MySqlCommand(query, cn);
        cmd.Parameters.AddWithValue("@p", partNumber);
        if (id.HasValue)
            cmd.Parameters.AddWithValue("@id", id.Value);

        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }
}