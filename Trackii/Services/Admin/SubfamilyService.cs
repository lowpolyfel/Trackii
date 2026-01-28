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

    // ===================== LISTADO =====================
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

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        vm.Areas = GetActiveAreas(cn);
        vm.Families = GetActiveFamilies(cn, areaId);

        var where = " WHERE 1=1 ";
        if (!showInactive) where += " AND s.active = 1 ";
        if (areaId.HasValue) where += " AND a.id = @area ";
        if (familyId.HasValue) where += " AND f.id = @family ";
        if (!string.IsNullOrWhiteSpace(search)) where += " AND s.name LIKE @search ";

        using var countCmd = new MySqlCommand($@"
            SELECT COUNT(*)
            FROM subfamily s
            JOIN family f ON f.id = s.id_family
            JOIN area a ON a.id = f.id_area
            {where}", cn);

        AddFilters(countCmd, areaId, familyId, search);
        var total = Convert.ToInt32(countCmd.ExecuteScalar());
        vm.TotalPages = (int)Math.Ceiling(total / (double)pageSize);

        using var cmd = new MySqlCommand($@"
            SELECT s.id, s.name, s.active,
                   f.name AS family,
                   a.name AS area
            FROM subfamily s
            JOIN family f ON f.id = s.id_family
            JOIN area a ON a.id = f.id_area
            {where}
            ORDER BY a.name, f.name, s.name
            LIMIT @off, @lim", cn);

        AddFilters(cmd, areaId, familyId, search);
        cmd.Parameters.AddWithValue("@off", (page - 1) * pageSize);
        cmd.Parameters.AddWithValue("@lim", pageSize);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            vm.Items.Add(new SubfamilyListVm.Row
            {
                Id = rd.GetUInt32("id"),
                Name = rd.GetString("name"),
                Active = rd.GetBoolean("active"),
                Family = rd.GetString("family"),
                Area = rd.GetString("area")
            });
        }

        return vm;
    }

    // ===================== CRUD =====================
    public SubfamilyEditVm? GetById(uint id)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(
            "SELECT id, id_family, name FROM subfamily WHERE id=@id", cn);

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

    public void Create(SubfamilyEditVm vm)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(
            "INSERT INTO subfamily (id_family, name, active) VALUES (@f,@n,1)", cn);

        cmd.Parameters.AddWithValue("@f", vm.FamilyId);
        cmd.Parameters.AddWithValue("@n", vm.Name);
        cmd.ExecuteNonQuery();
    }

    public void Update(SubfamilyEditVm vm)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(
            "UPDATE subfamily SET id_family=@f, name=@n WHERE id=@id", cn);

        cmd.Parameters.AddWithValue("@id", vm.Id);
        cmd.Parameters.AddWithValue("@f", vm.FamilyId);
        cmd.Parameters.AddWithValue("@n", vm.Name);
        cmd.ExecuteNonQuery();
    }

    // ===================== TOGGLE =====================
    public bool SetActive(uint id, bool active)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        if (active)
        {
            using var chk = new MySqlCommand(@"
                SELECT f.active
                FROM subfamily s
                JOIN family f ON f.id = s.id_family
                WHERE s.id=@id", cn);

            chk.Parameters.AddWithValue("@id", id);
            if (!Convert.ToBoolean(chk.ExecuteScalar()))
                return false;
        }

        using var cmd = new MySqlCommand(
            "UPDATE subfamily SET active=@a WHERE id=@id", cn);

        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@a", active);
        cmd.ExecuteNonQuery();
        return true;
    }

    // ===================== HELPERS =====================
    public List<(uint Id, string Name)> GetActiveFamilies()
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();
        return GetActiveFamilies(cn, null);
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

    private static void AddFilters(
        MySqlCommand cmd,
        uint? areaId,
        uint? familyId,
        string? search)
    {
        if (areaId.HasValue)
            cmd.Parameters.AddWithValue("@area", areaId);
        if (familyId.HasValue)
            cmd.Parameters.AddWithValue("@family", familyId);
        if (!string.IsNullOrWhiteSpace(search))
            cmd.Parameters.AddWithValue("@search", $"%{search}%");
    }
}
