using MySql.Data.MySqlClient;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace Trackii.Services;

public class AuthService
{
    private readonly string _connectionString;
    private readonly PasswordHasher<string> _hasher = new();

    public AuthService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("TrackiiDb")
            ?? throw new Exception("Connection string TrackiiDb not found");
    }

    public ClaimsPrincipal? Login(string username, string passwordPlain)
    {
        using var cn = new MySqlConnection(_connectionString);
        cn.Open();

        using var cmd = new MySqlCommand(@"
            SELECT u.username, u.password, r.name AS role
            FROM user u
            JOIN role r ON r.id = u.role_id
            WHERE u.username = @u
              AND u.active = 1
        ", cn);

        cmd.Parameters.AddWithValue("@u", username);

        using var rd = cmd.ExecuteReader();
        if (!rd.Read())
            return null;

        var storedHash = rd.GetString("password");

        var verify = _hasher.VerifyHashedPassword(
            username,
            storedHash,
            passwordPlain
        );

        if (verify == PasswordVerificationResult.Failed)
            return null;

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, rd.GetString("username")),
            new Claim(ClaimTypes.Role, rd.GetString("role"))
        };

        var identity = new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme
        );

        return new ClaimsPrincipal(identity);
    }
}
