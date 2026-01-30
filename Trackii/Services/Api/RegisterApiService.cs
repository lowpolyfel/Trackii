using Microsoft.AspNetCore.Identity;
using MySql.Data.MySqlClient;
using Trackii.Models.Api;

namespace Trackii.Services.Api;

public class RegisterApiService
{
    private readonly string _cs;

    public RegisterApiService(IConfiguration cfg)
    {
        _cs = cfg.GetConnectionString("TrackiiDb")!;
    }

    public RegisterResponseDto Register(RegisterRequestDto dto)
    {
        using var cn = new MySqlConnection(_cs);
        cn.Open();

        using var tx = cn.BeginTransaction();

        try
        {
            // 1) Validar token provisioning
            var tokenCmd = cn.CreateCommand();
            tokenCmd.Transaction = tx;
            tokenCmd.CommandText = "SELECT 1 FROM tokens WHERE code=@code";
            tokenCmd.Parameters.AddWithValue("@code", dto.Token);

            if (tokenCmd.ExecuteScalar() == null)
                throw new Exception("Token inválido");

            // 2) Resolver role Piso
            var roleCmd = cn.CreateCommand();
            roleCmd.Transaction = tx;
            roleCmd.CommandText = "SELECT id FROM role WHERE name='Piso' AND active=1";
            var roleId = Convert.ToUInt32(roleCmd.ExecuteScalar());

            // 3) Username automático
            var username = $"PISO-{dto.DeviceUid}";

            // 4) Crear usuario
            var hasher = new PasswordHasher<string>();
            var hash = hasher.HashPassword(username, dto.Password);

            var userCmd = cn.CreateCommand();
            userCmd.Transaction = tx;
            userCmd.CommandText = """
                INSERT INTO user (username, password, role_id, active)
                VALUES (@u, @p, @r, 1)
            """;
            userCmd.Parameters.AddWithValue("@u", username);
            userCmd.Parameters.AddWithValue("@p", hash);
            userCmd.Parameters.AddWithValue("@r", roleId);
            userCmd.ExecuteNonQuery();

            var userId = (uint)userCmd.LastInsertedId;

            // 5) Crear / actualizar device
            var deviceCmd = cn.CreateCommand();
            deviceCmd.Transaction = tx;
            deviceCmd.CommandText = """
                INSERT INTO devices (device_uid, name, location_id, active)
                VALUES (@uid, @name, @loc, 1)
                ON DUPLICATE KEY UPDATE
                    name = VALUES(name),
                    location_id = VALUES(location_id),
                    active = 1
            """;
            deviceCmd.Parameters.AddWithValue("@uid", dto.DeviceUid);
            deviceCmd.Parameters.AddWithValue("@name", (object?)dto.DeviceName ?? DBNull.Value);
            deviceCmd.Parameters.AddWithValue("@loc", dto.LocationId);
            deviceCmd.ExecuteNonQuery();

            var deviceId = (uint)deviceCmd.LastInsertedId;

            tx.Commit();

            return new RegisterResponseDto
            {
                UserId = userId,
                DeviceId = deviceId,
                Username = username,
                Jwt = "" // JWT se obtiene vía /auth/login
            };
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
