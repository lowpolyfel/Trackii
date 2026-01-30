using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using MySql.Data.MySqlClient;

namespace Trackii.Services;

public class AuthService
{
    private readonly string _conn;
    private readonly PasswordHasher<string> _hasher = new();

    public AuthService(IConfiguration cfg)
    {
        _conn = cfg.GetConnectionString("TrackiiDb")
            ?? throw new Exception("Connection string TrackiiDb no configurada");
    }

    // Para MVC (cookies) - se queda igual
    public ClaimsPrincipal? Login(string username, string passwordPlain)
    {
        var info = ValidateUser(username, passwordPlain);
        if (info == null) return null;

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, info.Value.Username),
            new Claim(ClaimTypes.Role, info.Value.Role)
        };

        var identity = new ClaimsIdentity(claims, "Cookies");
        return new ClaimsPrincipal(identity);
    }

    // Para API/JWT - NUEVO
    public (uint UserId, string Username, string Role)? ValidateUser(string username, string passwordPlain)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(@"
            SELECT u.id, u.username, u.password, u.active, r.name AS role
            FROM `user` u
            JOIN `role` r ON r.id = u.role_id
            WHERE u.username=@u
            LIMIT 1", cn);

        cmd.Parameters.AddWithValue("@u", username);

        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;

        var active = rd.GetBoolean("active");
        if (!active) return null;

        var userId = Convert.ToUInt32(rd.GetUInt32("id"));
        var dbUsername = rd.GetString("username");
        var dbHash = rd.GetString("password");
        var role = rd.GetString("role");

        PasswordVerificationResult verify;

        try
        {
            verify = _hasher.VerifyHashedPassword(dbUsername, dbHash, passwordPlain);
        }
        catch (FormatException)
        {
            return null;
        }

        if (verify == PasswordVerificationResult.Failed)
            return null;

        if (verify == PasswordVerificationResult.SuccessRehashNeeded)
        {
            var newHash = _hasher.HashPassword(dbUsername, passwordPlain);

            rd.Close();
            using var up = new MySqlCommand("UPDATE `user` SET password=@p WHERE username=@u", cn);
            up.Parameters.AddWithValue("@p", newHash);
            up.Parameters.AddWithValue("@u", dbUsername);
            up.ExecuteNonQuery();
        }

        return (userId, dbUsername, role);
    }
}
