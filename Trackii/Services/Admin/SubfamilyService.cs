using MySql.Data.MySqlClient;
using Trackii.Models.Admin.Subfamily;

namespace Trackii.Services.Admin;

public class SubfamilyService
{
    private readonly string _conn;

    public SubfamilyService(IConfiguration cfg)
    {
        _conn = cfg.GetConnectionString("TrackiiDb")
            ?? throw new Exception("Connection string TrackiiDb no configurada");
    }

    // ===================== LISTADO (CON TU LÓGICA DE VERSION) =====================
    public SubfamilyListVm GetPaged(
        uint? areaId,
        uint? familyId,
        string? search,
        bool showInactive,
        int page,
        int pageSize)
    {
        var vm = new SubfamilyListVm
        {
            AreaId = areaId,
            FamilyId = familyId,
            Search = search,
            ShowInactive = showInactive,
            Page = page
        };

        vm.Areas = GetActiveAreas();
        vm.Families = GetActiveFamilies();

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        var where = " WHERE 1=1 ";
        if (!showInactive) where += " AND s.active = 1 ";
        if (areaId.HasValue) where += " AND a.id = @area ";
        if (familyId.HasValue) where += " AND f.id = @family ";
        if (!string.IsNullOrWhiteSpace(search)) where += " AND s.name LIKE @search ";

        // COUNT
        using (var countCmd = new MySqlCommand($@"
            SELECT COUNT(*)
            FROM subfamily s
            JOIN family f ON f.id = s.id_family
            JOIN area a ON a.id = f.id_area
            {where}", cn))
        {
            AddFilters(countCmd, areaId, familyId, search);
            var total = Convert.ToInt32(countCmd.ExecuteScalar());
            vm.TotalPages = (int)Math.Ceiling(total / (double)pageSize);
        }

        // DATA
        using var cmd = new MySqlCommand($@"
            SELECT 
                s.id,
                s.name,
                s.active,
                f.name AS family_name,
                a.name AS area_name,
                r.version AS active_version 
            FROM subfamily s
            JOIN family f ON f.id = s.id_family
            JOIN area a ON a.id = f.id_area
            LEFT JOIN route r ON r.id = s.active_route_id
            {where}
            ORDER BY a.name, f.name, s.name
            LIMIT @off, @lim", cn);

        AddFilters(cmd, areaId, familyId, search);
        cmd.Parameters.AddWithValue("@off", (page - 1) * pageSize);
        cmd.Parameters.AddWithValue("@lim", pageSize);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var row = new SubfamilyListVm.Row
            {
                Id = rd.GetUInt32("id"),
                Name = rd.GetString("name"),
                AreaName = rd.GetString("area_name"),
                FamilyName = rd.GetString("family_name"),
                Active = rd.GetBoolean("active")
            };

            if (!rd.IsDBNull(rd.GetOrdinal("active_version")))
            {
                row.ActiveRouteVersion = rd.GetString("active_version");
            }

            vm.Rows.Add(row);
        }

        return vm;
    }

    // ===================== HELPERS DE CARGA =====================
    public List<(uint Id, string Name)> GetActiveAreas()
    {
        var list = new List<(uint, string)>();
        using var cn = new MySqlConnection(_conn);
        cn.Open();
        using var cmd = new MySqlCommand(
            "SELECT id, name FROM area WHERE active = 1 ORDER BY name", cn);
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) list.Add((rd.GetUInt32("id"), rd.GetString("name")));
        return list;
    }

    public List<(uint Id, string Name)> GetActiveFamilies()
    {
        var list = new List<(uint, string)>();
        using var cn = new MySqlConnection(_conn);
        cn.Open();
        using var cmd = new MySqlCommand(
            "SELECT id, name FROM family WHERE active = 1 ORDER BY name", cn);
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) list.Add((rd.GetUInt32("id"), rd.GetString("name")));
        return list;
    }

    // ===================== CRUD =====================
    public SubfamilyEditVm? GetById(uint id)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(@"
            SELECT id, id_family, name
            FROM subfamily
            WHERE id=@id", cn);

        cmd.Parameters.AddWithValue("@id", id);
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;

        return new SubfamilyEditVm
        {
            Id = id,
            FamilyId = rd.GetUInt32("id_family"),
            Name = rd.GetString("name")
        };
    }

    // ===================== CREATE (Con Validación) =====================
    public void Create(SubfamilyEditVm vm)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        // VALIDACIÓN: Padre (Familia) activa
        using (var check = new MySqlCommand("SELECT active FROM family WHERE id=@fid", cn))
        {
            check.Parameters.AddWithValue("@fid", vm.FamilyId);
            if (!Convert.ToBoolean(check.ExecuteScalar()))
                throw new Exception("No se puede crear: La Familia seleccionada está inactiva.");
        }

        using var cmd = new MySqlCommand(@"
            INSERT INTO subfamily (id_family, name, active)
            VALUES (@f,@n,1)", cn);

        cmd.Parameters.AddWithValue("@f", vm.FamilyId);
        cmd.Parameters.AddWithValue("@n", vm.Name);
        cmd.ExecuteNonQuery();
    }

    // ===================== UPDATE (Con Validación) =====================
    public void Update(SubfamilyEditVm vm)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        // VALIDACIÓN: Padre (Familia) activa
        using (var check = new MySqlCommand("SELECT active FROM family WHERE id=@fid", cn))
        {
            check.Parameters.AddWithValue("@fid", vm.FamilyId);
            if (!Convert.ToBoolean(check.ExecuteScalar()))
                throw new Exception("No se puede actualizar: La Familia seleccionada está inactiva.");
        }

        using var cmd = new MySqlCommand(@"
            UPDATE subfamily
            SET id_family=@f, name=@n
            WHERE id=@id", cn);

        cmd.Parameters.AddWithValue("@id", vm.Id);
        cmd.Parameters.AddWithValue("@f", vm.FamilyId);
        cmd.Parameters.AddWithValue("@n", vm.Name);
        cmd.ExecuteNonQuery();
    }

    // ==========================================
    // SET ACTIVE (Cascada Bidireccional)
    // ==========================================
    public bool SetActive(uint id, bool active)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();
        using var tx = cn.BeginTransaction();

        if (active)
        {
            // VALIDACIÓN: Verificar que la Familia (Padre) esté activa
            using var chk = new MySqlCommand(@"
                SELECT f.active
                FROM subfamily s
                JOIN family f ON f.id = s.id_family
                WHERE s.id=@id", cn, tx);

            chk.Parameters.AddWithValue("@id", id);
            var parentActive = chk.ExecuteScalar();

            if (parentActive == null || !Convert.ToBoolean(parentActive))
                return false; // Padre inactivo
        }

        // 1. Actualizar la Subfamilia
        using var cmd = new MySqlCommand(
            "UPDATE subfamily SET active=@a WHERE id=@id", cn, tx);

        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@a", active);
        cmd.ExecuteNonQuery();

        // 2. CASCADA: Productos y Rutas
        var val = active ? 1 : 0;

        // A. Actualizar Productos
        using (var offProd = new MySqlCommand(
            "UPDATE product SET active = @val WHERE id_subfamily = @id", cn, tx))
        {
            offProd.Parameters.AddWithValue("@val", val);
            offProd.Parameters.AddWithValue("@id", id);
            offProd.ExecuteNonQuery();
        }

        // B. Actualizar Rutas
        using (var offRoutes = new MySqlCommand(
            "UPDATE route SET active = @val WHERE subfamily_id = @id", cn, tx))
        {
            offRoutes.Parameters.AddWithValue("@val", val);
            offRoutes.Parameters.AddWithValue("@id", id);
            offRoutes.ExecuteNonQuery();
        }

        tx.Commit();
        return true;
    }
    public bool Exists(string name, uint? id = null)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        var query = "SELECT COUNT(*) FROM subfamily WHERE name = @name";

        if (id.HasValue)
            query += " AND id != @id";

        using var cmd = new MySqlCommand(query, cn);
        cmd.Parameters.AddWithValue("@name", name);
        if (id.HasValue)
            cmd.Parameters.AddWithValue("@id", id.Value);

        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private static void AddFilters(MySqlCommand cmd, uint? areaId, uint? familyId, string? search)
    {
        if (areaId.HasValue) cmd.Parameters.AddWithValue("@area", areaId);
        if (familyId.HasValue) cmd.Parameters.AddWithValue("@family", familyId);
        if (!string.IsNullOrWhiteSpace(search)) cmd.Parameters.AddWithValue("@search", $"%{search}%");
    }
}