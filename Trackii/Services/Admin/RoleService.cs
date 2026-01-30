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

    public RoleListVm GetPaged(string? search, bool showInactive, int page, int pageSize)
    {
        var vm = new RoleListVm
        {
            Search = search,
            ShowInactive = showInactive,
            Page = page
        };

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        var where = " WHERE 1=1 ";
        if (!showInactive) where += " AND r.active = 1 ";
        if (!string.IsNullOrWhiteSpace(search)) where += " AND r.name LIKE @s ";

        using var countCmd = new MySqlCommand($@"SELECT COUNT(*) FROM `role` r {where}", cn);
        if (!string.IsNullOrWhiteSpace(search))
            countCmd.Parameters.AddWithValue("@s", $"%{search}%");

        var total = Convert.ToInt32(countCmd.ExecuteScalar());
        vm.TotalPages = (int)Math.Ceiling(total / (double)pageSize);

        using var cmd = new MySqlCommand($@"
            SELECT r.id, r.name, r.active
            FROM `role` r
            {where}
            ORDER BY r.name
            LIMIT @off,@lim", cn);

        if (!string.IsNullOrWhiteSpace(search))
            cmd.Parameters.AddWithValue("@s", $"%{search}%");

        cmd.Parameters.AddWithValue("@off", (page - 1) * pageSize);
        cmd.Parameters.AddWithValue("@lim", pageSize);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            vm.Items.Add(new RoleListVm.Row
            {
                Id = rd.GetUInt32("id"),
                Name = rd.GetString("name"),
                Active = rd.GetBoolean("active")
            });
        }

        return vm;
    }

    public RoleEditVm? GetById(uint id)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand("SELECT id, name FROM `role` WHERE id=@id", cn);
        cmd.Parameters.AddWithValue("@id", id);

        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;

        return new RoleEditVm
        {
            Id = rd.GetUInt32("id"),
            Name = rd.GetString("name")
        };
    }

    public void Create(RoleEditVm vm)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand("INSERT INTO `role` (name, active) VALUES (@n, 1)", cn);
        cmd.Parameters.AddWithValue("@n", vm.Name);
        cmd.ExecuteNonQuery();
    }

    // Mantengo tu regla: si está en uso, no permitir editar el nombre
    public bool Update(RoleEditVm vm)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var chk = new MySqlCommand("SELECT COUNT(*) FROM `user` WHERE role_id=@id", cn);
        chk.Parameters.AddWithValue("@id", vm.Id);

        if (Convert.ToInt32(chk.ExecuteScalar()) > 0)
            return false;

        using var cmd = new MySqlCommand("UPDATE `role` SET name=@n WHERE id=@id", cn);
        cmd.Parameters.AddWithValue("@id", vm.Id);
        cmd.Parameters.AddWithValue("@n", vm.Name);
        cmd.ExecuteNonQuery();
        return true;
    }

    // No desactivar si hay usuarios ACTIVOS usando ese role
    public bool Toggle(uint id)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        // si está activo, validar que no haya usuarios activos
        using var getActive = new MySqlCommand("SELECT active FROM `role` WHERE id=@id", cn);
        getActive.Parameters.AddWithValue("@id", id);
        var isActive = Convert.ToInt32(getActive.ExecuteScalar()) == 1;

        if (isActive)
        {
            using var chk = new MySqlCommand("SELECT COUNT(*) FROM `user` WHERE role_id=@id AND active=1", cn);
            chk.Parameters.AddWithValue("@id", id);
            if (Convert.ToInt32(chk.ExecuteScalar()) > 0)
                return false;
        }

        using var cmd = new MySqlCommand("UPDATE `role` SET active = NOT active WHERE id=@id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
        return true;
    }
   

    // VALIDACIÓN DE DUPLICADOS
    public bool Exists(string name, uint? id = null)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        var query = "SELECT COUNT(*) FROM role WHERE name = @n";

        if (id.HasValue)
            query += " AND id != @id";

        using var cmd = new MySqlCommand(query, cn);
        cmd.Parameters.AddWithValue("@n", name);
        if (id.HasValue)
            cmd.Parameters.AddWithValue("@id", id.Value);

        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }
}
