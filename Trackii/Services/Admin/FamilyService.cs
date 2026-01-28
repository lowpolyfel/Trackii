using MySql.Data.MySqlClient;
using Trackii.Models.Admin.Family;

namespace Trackii.Services.Admin;

public class FamilyService
{
    private readonly string _conn;

    public FamilyService(IConfiguration cfg)
    {
        _conn = cfg.GetConnectionString("TrackiiDb")!;
    }

    public List<(uint Id, string Name)> GetActiveAreas()
    {
        var list = new List<(uint, string)>();

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(
            "SELECT id, name FROM area WHERE active = 1 ORDER BY name", cn);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            list.Add((rd.GetUInt32("id"), rd.GetString("name")));

        return list;
    }

    public FamilyListVm GetPaged(
        uint? areaId,
        string? search,
        bool showInactive,
        int page,
        int pageSize)
    {
        var vm = new FamilyListVm
        {
            AreaId = areaId,
            Search = search,
            ShowInactive = showInactive,
            Page = page,
            PageSize = pageSize
        };

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        vm.Areas = GetActiveAreas();

        var where = "WHERE 1=1 ";
        if (areaId.HasValue)
            where += "AND f.id_area = @area ";
        if (!string.IsNullOrWhiteSpace(search))
            where += "AND f.name LIKE @search ";
        if (!showInactive)
            where += "AND f.active = 1 ";

        using (var cmd = new MySqlCommand($@"
            SELECT COUNT(*)
            FROM family f
            JOIN area a ON a.id = f.id_area
            {where}", cn))
        {
            if (areaId.HasValue)
                cmd.Parameters.AddWithValue("@area", areaId.Value);
            if (!string.IsNullOrWhiteSpace(search))
                cmd.Parameters.AddWithValue("@search", $"%{search}%");
            vm.TotalRows = Convert.ToInt32(cmd.ExecuteScalar());
        }

        var offset = (page - 1) * pageSize;

        using (var cmd = new MySqlCommand($@"
            SELECT f.id, f.name, f.active, a.name AS area
            FROM family f
            JOIN area a ON a.id = f.id_area
            {where}
            ORDER BY a.name, f.name
            LIMIT @ps OFFSET @off", cn))
        {
            if (areaId.HasValue)
                cmd.Parameters.AddWithValue("@area", areaId.Value);
            if (!string.IsNullOrWhiteSpace(search))
                cmd.Parameters.AddWithValue("@search", $"%{search}%");

            cmd.Parameters.AddWithValue("@ps", pageSize);
            cmd.Parameters.AddWithValue("@off", offset);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                vm.Items.Add(new FamilyRowVm
                {
                    Id = rd.GetUInt32("id"),
                    Name = rd.GetString("name"),
                    AreaName = rd.GetString("area"),
                    Active = rd.GetBoolean("active")
                });
            }
        }

        return vm;
    }

    public FamilyEditVm? GetById(uint id)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(
            "SELECT id, id_area, name, active FROM family WHERE id = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);

        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;

        return new FamilyEditVm
        {
            Id = rd.GetUInt32("id"),
            AreaId = rd.GetUInt32("id_area"),
            Name = rd.GetString("name"),
            Active = rd.GetBoolean("active")
        };
    }

    public void Create(FamilyEditVm vm)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(
            "INSERT INTO family (id_area, name, active) VALUES (@a, @n, 1)", cn);
        cmd.Parameters.AddWithValue("@a", vm.AreaId);
        cmd.Parameters.AddWithValue("@n", vm.Name);
        cmd.ExecuteNonQuery();
    }

    public void Update(FamilyEditVm vm)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(
            "UPDATE family SET id_area = @a, name = @n WHERE id = @id", cn);
        cmd.Parameters.AddWithValue("@a", vm.AreaId);
        cmd.Parameters.AddWithValue("@n", vm.Name);
        cmd.Parameters.AddWithValue("@id", vm.Id);
        cmd.ExecuteNonQuery();
    }

    public bool SetActive(uint id, bool active)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        if (active)
        {
            // Verificar que el Area esté activa
            using var check = new MySqlCommand(@"
            SELECT a.active
            FROM family f
            JOIN area a ON a.id = f.id_area
            WHERE f.id = @id
        ", cn);

            check.Parameters.AddWithValue("@id", id);

            var areaActive = Convert.ToBoolean(check.ExecuteScalar());
            if (!areaActive)
            {
                // NO permitir activar
                return false;
            }
        }

        using var cmd = new MySqlCommand(
            "UPDATE family SET active = @a WHERE id = @id", cn);

        cmd.Parameters.AddWithValue("@a", active);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();

        return true;
    }

}
