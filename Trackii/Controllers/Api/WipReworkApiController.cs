using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trackii.Models.Api;
using Trackii.Services.Api;

namespace Trackii.Controllers.Api;

[ApiController]
[Route("api/v1/wip")]
[Authorize(AuthenticationSchemes = "ApiBearer")]
public class WipReworkApiController : ControllerBase
{
    private readonly ScanApiService _scan;

    public WipReworkApiController(ScanApiService scan)
    {
        _scan = scan;
    }

    [HttpPost("rework")]
    public IActionResult Rework([FromBody] WipReworkRequest req)
    {
        var uid = User.FindFirstValue("uid");
        if (string.IsNullOrWhiteSpace(uid) || !uint.TryParse(uid, out var userId))
            return Unauthorized("Token sin uid");

        var r = _scan.StartRework(
            userId,
            req.DeviceId,
            req.Lot.Trim(),
            req.PartNumber.Trim(),
            req.LocationId,
            req.Qty,
            req.Reason?.Trim()
        );

        if (!r.Ok)
            return BadRequest(r.Reason);

        return Ok(r);
    }

    [HttpPost("rework/release")]
    public IActionResult Release([FromBody] WipReworkReleaseRequest req)
    {
        var r = _scan.ReleaseRework(
            req.Lot.Trim(),
            req.PartNumber.Trim()
        );

        if (!r.Ok)
            return BadRequest(r.Reason);

        return Ok(r);
    }
}
