using MySql.Data.MySqlClient;
using BCrypt.Net;
using Trackii.Models.Admin.User;

namespace Trackii.Services.Admin;

public class UserService
{
    private readonly string _conn;

    public UserService(IConfiguration cfg)
    {
        _conn = cfg.GetConnectionString("TrackiiDb")!;
    }

    public UserListVm GetPaged(string? search, int page, int pageSize)
    {
        var vm = new UserListVm
        {
            Search = search,
            Page = page
        };

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        var where = " WHERE 1=1 ";
        if (!string.IsNullOrWhiteSpace(search))
            where += " AND u.username LIKE @s ";

        using var cmd = new MySqlCommand($@"
            SELECT u.id, u.username, r.name AS role, u.active
            FROM user u
            JOIN role r ON r.id = u.role_id
            {where}
            ORDER BY u.username
            LIMIT @off,@sz", cn);

        if (!string.IsNullOrWhiteSpace(search))
            cmd.Parameters.AddWithValue("@s", $"%{search}%");

        cmd.Parameters.AddWithValue("@off", (page - 1) * pageSize);
        cmd.Parameters.AddWithValue("@sz", pageSize);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            vm.Items.Add(new UserListVm.Row
            {
                Id = rd.GetUInt32("id"),
                Username = rd.GetString("username"),
                Role = rd.GetString("role"),
                Active = rd.GetBoolean("active")
            });
        }
        rd.Close();

        using var cnt = new MySqlCommand($"SELECT COUNT(*) FROM user u {where}", cn);
        if (!string.IsNullOrWhiteSpace(search))
            cnt.Parameters.AddWithValue("@s", $"%{search}%");

        var total = Convert.ToInt32(cnt.ExecuteScalar());
        vm.TotalPages = (int)Math.Ceiling(total / (double)pageSize);

        return vm;
    }

    public UserEditVm? GetById(uint id)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(@"
            SELECT id, username, role_id, active
            FROM user
            WHERE id=@id", cn);

        cmd.Parameters.AddWithValue("@id", id);

        using var rd = cmd.ExecuteReader();
        if (!rd.Read())
            return null;

        return new UserEditVm
        {
            Id = rd.GetUInt32("id"),
            Username = rd.GetString("username"),
            RoleId = rd.GetUInt32("role_id"),
            Active = rd.GetBoolean("active")
        };
    }

    public void Update(UserEditVm vm)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(@"
            UPDATE user
            SET username=@u, role_id=@r, active=@a
            WHERE id=@id", cn);

        cmd.Parameters.AddWithValue("@u", vm.Username);
        cmd.Parameters.AddWithValue("@r", vm.RoleId);
        cmd.Parameters.AddWithValue("@a", vm.Active);
        cmd.Parameters.AddWithValue("@id", vm.Id);
        cmd.ExecuteNonQuery();

        if (!string.IsNullOrWhiteSpace(vm.NewPassword))
        {
            var hash = BCrypt.Net.BCrypt.HashPassword(vm.NewPassword);

            using var pwd = new MySqlCommand(
                "UPDATE user SET password=@p WHERE id=@id", cn);

            pwd.Parameters.AddWithValue("@p", hash);
            pwd.Parameters.AddWithValue("@id", vm.Id);
            pwd.ExecuteNonQuery();
        }
    }

    public void Toggle(uint id)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(@"
            UPDATE user
            SET active = NOT active
            WHERE id=@id", cn);

        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }
}
