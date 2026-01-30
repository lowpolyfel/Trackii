using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trackii.Models.Api;
using Trackii.Services;
using Trackii.Services.Api;

namespace Trackii.Controllers.Api;

[ApiController]
[AllowAnonymous]
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

    // POST /api/v1/auth/login
    [HttpPost("login")]
    public ActionResult<LoginResponse> Login([FromBody] LoginRequest req)
    {
        if (req == null) return BadRequest("request requerido");

        var username = (req.Username ?? "").Trim();
        var password = req.Password ?? "";

        if (username.Length == 0) return BadRequest("username requerido");
        if (password.Length == 0) return BadRequest("password requerido");

        var info = _auth.ValidateUser(username, password);
        if (info == null)
            return Unauthorized(new { error = "INVALID_CREDENTIALS" });

        var token = _jwt.CreateToken(info.Value.UserId, info.Value.Username, info.Value.Role);

        return Ok(new LoginResponse
        {
            Token = token.Token,
            UserId = info.Value.UserId,
            Role = info.Value.Role,
            ExpiresAtUtc = token.ExpiresUtc
        });
    }
}
