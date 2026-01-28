using MySql.Data.MySqlClient;
using Trackii.Models.Admin.Role;

namespace Trackii.Services.Admin;

public class RoleService
{
    private readonly string _conn;

    public RoleService(IConfiguration cfg)
    {
        _conn = cfg.GetConnectionString("TrackiiDb")
            ?? throw new Exception("Connection string TrackiiDb no configurada");
    }

    public List<RoleListVm.Row> GetAll()
    {
        var list = new List<RoleListVm.Row>();

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(
            "SELECT id,name FROM role ORDER BY name", cn);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            list.Add(new RoleListVm.Row
            {
                Id = rd.GetUInt32("id"),
                Name = rd.GetString("name")
            });
        }

        return list;
    }

    public RoleEditVm? GetById(uint id)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(
            "SELECT id,name FROM role WHERE id=@id", cn);

        cmd.Parameters.AddWithValue("@id", id);
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;

        return new RoleEditVm
        {
            Id = id,
            Name = rd.GetString("name")
        };
    }

    public void Create(RoleEditVm vm)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(
            "INSERT INTO role (name) VALUES (@n)", cn);

        cmd.Parameters.AddWithValue("@n", vm.Name);
        cmd.ExecuteNonQuery();
    }

    public bool Update(RoleEditVm vm)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var chk = new MySqlCommand(
            "SELECT COUNT(*) FROM user WHERE role_id=@id", cn);

        chk.Parameters.AddWithValue("@id", vm.Id);
        if (Convert.ToInt32(chk.ExecuteScalar()) > 0)
            return false;

        using var cmd = new MySqlCommand(
            "UPDATE role SET name=@n WHERE id=@id", cn);

        cmd.Parameters.AddWithValue("@id", vm.Id);
        cmd.Parameters.AddWithValue("@n", vm.Name);
        cmd.ExecuteNonQuery();
        return true;
    }
}
