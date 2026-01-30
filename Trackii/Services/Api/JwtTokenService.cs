using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Trackii.Services.Api;

public class JwtTokenService
{
    private readonly IConfiguration _cfg;

    public JwtTokenService(IConfiguration cfg)
    {
        _cfg = cfg;
    }

    public (string Token, DateTime ExpiresUtc) CreateToken(uint userId, string username, string role)
    {
        var jwt = _cfg.GetSection("Jwt");
        var issuer = jwt["Issuer"] ?? "Trackii";
        var audience = jwt["Audience"] ?? "Trackii.Tablets";
        var secret = jwt["Secret"] ?? throw new Exception("Jwt:Secret no configurado");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var expires = DateTime.UtcNow.AddHours(12);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role),
            new Claim("uid", userId.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds
        );

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
