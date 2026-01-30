using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trackii.Models.Api;
using Trackii.Services.Api;

namespace Trackii.Controllers.Api;

[ApiController]
[Route("api/v1/wip")]
[Authorize(AuthenticationSchemes = "ApiBearer")]
public class WipCancelApiController : ControllerBase
{
    private readonly ScanApiService _scan;

    public WipCancelApiController(ScanApiService scan)
    {
        _scan = scan;
    }

    [HttpPost("cancel")]
    public IActionResult Cancel([FromBody] WipCancelRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Lot))
            return BadRequest("lot requerido");

        if (string.IsNullOrWhiteSpace(req.PartNumber))
            return BadRequest("partNumber requerido");

        if (req.DeviceId == 0)
            return BadRequest("deviceId requerido");

        var uid = User.FindFirstValue("uid");
        if (string.IsNullOrWhiteSpace(uid) || !uint.TryParse(uid, out var userId))
            return Unauthorized("Token sin uid");

        var r = _scan.CancelWip(
            userId,
            req.DeviceId,
            req.Lot.Trim(),
            req.PartNumber.Trim(),
            req.Reason?.Trim()
        );

        if (!r.Ok)
            return BadRequest(r.Reason);

        return Ok(r);
    }
}
