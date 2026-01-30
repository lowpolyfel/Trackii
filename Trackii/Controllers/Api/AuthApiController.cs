using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trackii.Models.Api;
using Trackii.Services;
using Trackii.Services.Api;

namespace Trackii.Controllers.Api;

[ApiController]
[Route("api/v1/auth")]
public class AuthApiController : ControllerBase
{
    private readonly AuthService _auth;
    private readonly JwtTokenService _jwt;

    public AuthApiController(AuthService auth, JwtTokenService jwt)
    {
        _auth = auth;
        _jwt = jwt;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public ActionResult<LoginResponse> Login([FromBody] LoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest("Username/password requeridos.");

        var info = _auth.ValidateUser(req.Username.Trim(), req.Password);
        if (info == null) return Unauthorized();

        var (token, exp) = _jwt.CreateToken(info.Value.UserId, info.Value.Username, info.Value.Role);

        return Ok(new LoginResponse
        {
            Token = token,
            UserId = info.Value.UserId,
            Role = info.Value.Role,
            ExpiresAtUtc = exp
        });
    }
}
