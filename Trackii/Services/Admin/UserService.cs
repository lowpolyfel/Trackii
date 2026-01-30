using Microsoft.AspNetCore.Identity;
using MySql.Data.MySqlClient;
using Trackii.Models.Admin.User;

namespace Trackii.Services.Admin
{
    public class UserService
    {
        private readonly string _conn;
        private readonly PasswordHasher<string> _hasher = new();

        private const int MinLen = 6;
        private const bool RequireDigit = true;
        private const bool RequireLower = true;
        private const bool RequireUpper = true;
        private const bool RequireNonAlnum = true;

        public UserService(IConfiguration config)
        {
            _conn = config.GetConnectionString("TrackiiDb")
                ?? throw new Exception("Connection string TrackiiDb no configurada");
        }

        // ================= LIST =================
        public UserListVm GetPaged(string? search, bool showInactive, int page, int pageSize)
        {
            if (page < 1) page = 1;

            var vm = new UserListVm
            {
                Search = search,
                ShowInactive = showInactive,
                Page = page
            };

            using var cn = new MySqlConnection(_conn);
            cn.Open();

            var where = "WHERE 1=1 ";
            if (!showInactive)
                where += "AND u.active = 1 ";
            if (!string.IsNullOrWhiteSpace(search))
                where += "AND u.username LIKE @search ";

            using (var countCmd = new MySqlCommand($@"
                SELECT COUNT(*)
                FROM user u
                {where}", cn))
            {
                if (!string.IsNullOrWhiteSpace(search))
                    countCmd.Parameters.AddWithValue("@search", $"%{search}%");

                var total = Convert.ToInt32(countCmd.ExecuteScalar());
                vm.TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
            }

            if (vm.TotalPages > 0 && vm.Page > vm.TotalPages)
                vm.Page = vm.TotalPages;

            using (var cmd = new MySqlCommand($@"
                SELECT u.id, u.username, u.active, r.name
                FROM user u
                JOIN role r ON r.id = u.role_id
                {where}
                ORDER BY u.username
                LIMIT @off, @size", cn))
            {
                cmd.Parameters.AddWithValue("@off", (vm.Page - 1) * pageSize);
                cmd.Parameters.AddWithValue("@size", pageSize);

                if (!string.IsNullOrWhiteSpace(search))
                    cmd.Parameters.AddWithValue("@search", $"%{search}%");

                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    vm.Rows.Add(new UserRowVm
                    {
                        Id = rd.GetUInt32(0),
                        Username = rd.GetString(1),
                        Active = rd.GetBoolean(2),
                        Role = rd.GetString(3)
                    });
                }
            }

            return vm;
        }

        // ================= GET BY ID =================
        public UserEditVm? GetById(uint id)
        {
            using var cn = new MySqlConnection(_conn);
            cn.Open();

            using var cmd = new MySqlCommand(@"
                SELECT id, username, role_id, active
                FROM user
                WHERE id = @id", cn);

            cmd.Parameters.AddWithValue("@id", id);

            using var rd = cmd.ExecuteReader();
            if (!rd.Read()) return null;

            return new UserEditVm
            {
                Id = rd.GetUInt32(0),
                Username = rd.GetString(1),
                RoleId = rd.GetUInt32(2),
                Active = rd.GetBoolean(3)
            };
        }

        // ================= ROLES =================
        public List<(uint Id, string Name)> GetRoles()
        {
            var list = new List<(uint, string)>();

            using var cn = new MySqlConnection(_conn);
            cn.Open();

            using var cmd = new MySqlCommand(
                "SELECT id, name FROM role ORDER BY name", cn);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                list.Add((rd.GetUInt32(0), rd.GetString(1)));

            return list;
        }

        // ================= CREATE =================
        public async Task<List<string>> CreateAsync(UserCreateVm vm)
        {
            vm.Username = (vm.Username ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(vm.Username))
                return new List<string> { "El username es requerido." };

            // 1. Validar Password
            var pwdErrors = ValidatePassword(vm.Password);
            if (pwdErrors.Count > 0)
                return pwdErrors;

            // 2. Validar Duplicado MANUALMENTE antes de intentar insertar
            if (Exists(vm.Username))
            {
                return new List<string> { "Ya existe un usuario con ese username." };
            }

            var hash = _hasher.HashPassword(vm.Username, vm.Password);

            using var cn = new MySqlConnection(_conn);
            cn.Open();

            using var cmd = new MySqlCommand(@"
                INSERT INTO user (username, password, role_id, active)
                VALUES (@u, @p, @r, 1)", cn);

            cmd.Parameters.AddWithValue("@u", vm.Username);
            cmd.Parameters.AddWithValue("@p", hash);
            cmd.Parameters.AddWithValue("@r", vm.RoleId);
            await cmd.ExecuteNonQueryAsync();

            return new();
        }

        // ================= UPDATE =================
        public async Task<List<string>> UpdateAsync(UserEditVm vm)
        {
            vm.Username = (vm.Username ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(vm.Username))
                return new List<string> { "El username es requerido." };

            // 1. Validar Duplicado MANUALMENTE (excluyendo el id actual)
            if (Exists(vm.Username, vm.Id))
            {
                return new List<string> { "Ya existe un usuario con ese username." };
            }

            string? newHash = null;

            if (!string.IsNullOrWhiteSpace(vm.NewPassword))
            {
                var pwdErrors = ValidatePassword(vm.NewPassword);
                if (pwdErrors.Count > 0)
                    return pwdErrors;

                newHash = _hasher.HashPassword(vm.Username, vm.NewPassword);
            }

            using var cn = new MySqlConnection(_conn);
            cn.Open();

            var sql = @"
                UPDATE user
                SET username=@u,
                    role_id=@r,
                    active=@a";

            if (newHash != null)
                sql += ", password=@p";

            sql += " WHERE id=@id";

            using var cmd = new MySqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@u", vm.Username);
            cmd.Parameters.AddWithValue("@r", vm.RoleId);
            cmd.Parameters.AddWithValue("@a", vm.Active);
            cmd.Parameters.AddWithValue("@id", vm.Id);
            if (newHash != null)
                cmd.Parameters.AddWithValue("@p", newHash);

            await cmd.ExecuteNonQueryAsync();

            return new();
        }

        // ================= TOGGLE =================
        public void Toggle(uint id)
        {
            using var cn = new MySqlConnection(_conn);
            cn.Open();

            using var cmd = new MySqlCommand(
                "UPDATE user SET active = NOT active WHERE id=@id", cn);

            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        // ================= HELPERS PRIVADOS =================
        private bool Exists(string username, uint? id = null)
        {
            using var cn = new MySqlConnection(_conn);
            cn.Open();
            var query = "SELECT COUNT(*) FROM user WHERE username = @u";
            if (id.HasValue) query += " AND id != @id";

            using var cmd = new MySqlCommand(query, cn);
            cmd.Parameters.AddWithValue("@u", username);
            if (id.HasValue) cmd.Parameters.AddWithValue("@id", id.Value);

            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        private static List<string> ValidatePassword(string? password)
        {
            var errors = new List<string>();
            password ??= string.Empty;

            if (password.Length < MinLen)
                errors.Add($"La contraseña debe tener al menos {MinLen} caracteres.");

            if (RequireDigit && !password.Any(char.IsDigit))
                errors.Add("La contraseña debe contener al menos un dígito.");

            if (RequireLower && !password.Any(char.IsLower))
                errors.Add("La contraseña debe contener al menos una minúscula.");

            if (RequireUpper && !password.Any(char.IsUpper))
                errors.Add("La contraseña debe contener al menos una mayúscula.");

            if (RequireNonAlnum && password.All(char.IsLetterOrDigit))
                errors.Add("La contraseña debe contener al menos un caracter especial.");

            if (password.Contains(' '))
                errors.Add("La contraseña no debe contener espacios.");

            return errors;
        }
    }
}