using Microsoft.AspNetCore.Authorization; // <--- IMPORTANTE
using Microsoft.AspNetCore.Mvc;
using Trackii.Models.Api;
using Trackii.Services.Api;

namespace Trackii.Controllers.Api;

[ApiController]
[AllowAnonymous]
[Route("api/v1/register")]
public class RegisterApiController : ControllerBase
{
    private readonly RegisterApiService _svc;

    public RegisterApiController(RegisterApiService svc)
    {
        _svc = svc;
    }

    [HttpPost]
    public ActionResult<RegisterResponseDto> Register(RegisterRequestDto dto)
    {
        try
        {
            return Ok(_svc.Register(dto));
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
