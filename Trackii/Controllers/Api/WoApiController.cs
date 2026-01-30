using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trackii.Models.Api;
using Trackii.Services.Api;

namespace Trackii.Controllers.Api;

[ApiController]
[Route("api/v1/wo")]
[Authorize(AuthenticationSchemes = "ApiBearer")]
public class WoApiController : ControllerBase
{
    private readonly ScanApiService _scan;

    public WoApiController(ScanApiService scan)
    {
        _scan = scan;
    }

    [HttpGet("{woNumber}/quick")]
    public ActionResult<WoQuickStatusResponse> Quick(string woNumber)
    {
        woNumber = (woNumber ?? "").Trim();
        if (woNumber.Length == 0) return BadRequest("woNumber requerido.");

        var r = _scan.GetQuickStatus(woNumber);
        if (r == null) return NotFound();

        return Ok(new WoQuickStatusResponse
        {
            WoNumber = r.WoNumber,
            HasWip = r.HasWip,
            Status = r.Status,
            CurrentStep = r.CurrentStep,
            ExpectedLocation = r.ExpectedLocation,
            QtyMax = r.QtyMax
        });
    }
}
