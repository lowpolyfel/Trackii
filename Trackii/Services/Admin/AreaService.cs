using MySql.Data.MySqlClient;
using Trackii.Models.Admin.Area;

namespace Trackii.Services.Admin;

public class AreaService
{
    private readonly string _conn;

    public AreaService(IConfiguration cfg)
    {
        _conn = cfg.GetConnectionString("TrackiiDb")!;
    }

    public AreaListVm GetPaged(string? search, bool showInactive, int page, int pageSize)
    {
        var vm = new AreaListVm
        {
            Search = search,
            ShowInactive = showInactive,
            Page = page,
            PageSize = pageSize
        };

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        var where = "WHERE 1=1 ";
        if (!string.IsNullOrWhiteSpace(search))
            where += "AND name LIKE @search ";
        if (!showInactive)
            where += "AND active = 1 ";

        using (var cmd = new MySqlCommand($"SELECT COUNT(*) FROM area {where}", cn))
        {
            if (!string.IsNullOrWhiteSpace(search))
                cmd.Parameters.AddWithValue("@search", $"%{search}%");
            vm.TotalRows = Convert.ToInt32(cmd.ExecuteScalar());
        }

        var offset = (page - 1) * pageSize;

        using (var cmd = new MySqlCommand($@"
            SELECT id, name, active
            FROM area
            {where}
            ORDER BY name
            LIMIT @ps OFFSET @off", cn))
        {
            if (!string.IsNullOrWhiteSpace(search))
                cmd.Parameters.AddWithValue("@search", $"%{search}%");
            cmd.Parameters.AddWithValue("@ps", pageSize);
            cmd.Parameters.AddWithValue("@off", offset);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                vm.Items.Add(new AreaRowVm
                {
                    Id = rd.GetUInt32("id"),
                    Name = rd.GetString("name"),
                    Active = rd.GetBoolean("active")
                });
            }
        }

        return vm;
    }

    public AreaEditVm? GetById(uint id)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(
            "SELECT id, name, active FROM area WHERE id = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);

        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;

        return new AreaEditVm
        {
            Id = rd.GetUInt32("id"),
            Name = rd.GetString("name"),
            Active = rd.GetBoolean("active")
        };
    }

    public void Create(AreaEditVm vm)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(
            "INSERT INTO area (name, active) VALUES (@n, 1)", cn);
        cmd.Parameters.AddWithValue("@n", vm.Name);
        cmd.ExecuteNonQuery();
    }

    public void Update(AreaEditVm vm)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(
            "UPDATE area SET name = @n WHERE id = @id", cn);
        cmd.Parameters.AddWithValue("@n", vm.Name);
        cmd.Parameters.AddWithValue("@id", vm.Id);
        cmd.ExecuteNonQuery();
    }

    public void SetActive(uint id, bool active)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();
        using var tx = cn.BeginTransaction();

        using (var cmd = new MySqlCommand(
            "UPDATE area SET active = @a WHERE id = @id", cn, tx))
        {
            cmd.Parameters.AddWithValue("@a", active);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        if (!active)
        {
            using var cmd = new MySqlCommand(
                "UPDATE family SET active = 0 WHERE id_area = @id", cn, tx);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }
}
