using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trackii.Models.Api;
using Trackii.Services.Api;

namespace Trackii.Controllers.Api;

[ApiController]
[Route("api/v1")]
[Authorize(AuthenticationSchemes = "ApiBearer")]
public class ScanApiController : ControllerBase
{
    private readonly ScanApiService _scan;
    private readonly ProductLookupService _lookupSvc;
    public ScanApiController(ScanApiService scanSvc, ProductLookupService lookupSvc)
    {
        _scan = scanSvc;
        _lookupSvc = lookupSvc;
    }

    // GET /api/v1/scan/resolve?deviceId=1&lot=1234567&partNumber=ABC
    [HttpGet("scan/resolve")]
    public ActionResult<ScanResolveResponse> Resolve([FromQuery] uint deviceId, [FromQuery] string lot, [FromQuery] string partNumber)
    {
        if (deviceId == 0) return BadRequest("deviceId requerido");
        lot = (lot ?? "").Trim();
        partNumber = (partNumber ?? "").Trim();

        if (lot.Length == 0) return BadRequest("lot requerido");
        if (!Regex.IsMatch(lot, @"^\d{7}$")) return BadRequest("lot invalido");
        if (partNumber.Length == 0) return BadRequest("partNumber requerido");

        var uid = User.FindFirstValue("uid");
        if (string.IsNullOrWhiteSpace(uid) || !uint.TryParse(uid, out var userId))
            return Unauthorized("Token sin uid");

        var r = _scan.Resolve(userId, deviceId, lot, partNumber);
        if (!r.Ok) return BadRequest(r.Reason);

        return Ok(r);
    }

    // POST /api/v1/scan  (commit)
    [HttpPost("scan")]
    public ActionResult<ScanResponse> Scan([FromBody] ScanRequest req)
    {
        if (req.DeviceId == 0)
            return BadRequest("deviceId requerido");

        if (string.IsNullOrWhiteSpace(req.Lot))
            return BadRequest("lot requerido");

        if (!Regex.IsMatch(req.Lot, @"^\d{7}$"))
            return BadRequest("lot invalido");

        if (string.IsNullOrWhiteSpace(req.PartNumber))
            return BadRequest("partNumber requerido");

        var uid = User.FindFirstValue("uid");
        if (string.IsNullOrWhiteSpace(uid) || !uint.TryParse(uid, out var userId))
            return Unauthorized("Token sin uid");

        var r = _scan.Scan(
            userId,
            req.DeviceId,
            req.Lot.Trim(),
            req.PartNumber.Trim(),
            req.Qty
        );

        return Ok(new ScanResponse
        {
            Ok = r.Ok,
            Advanced = r.Advanced,
            Status = r.Status,
            Reason = r.Reason,
            CurrentStep = r.CurrentStep,
            ExpectedLocation = r.ExpectedLocation,
            QtyIn = r.QtyIn,
            PreviousQty = r.PreviousQty,
            Scrap = r.Scrap,
            NextStep = r.NextStep,
            NextLocation = r.NextLocation
        });
    }

    [HttpGet("product/{partNumber}")]
    public IActionResult GetProductInfo(string partNumber)
    {
        try
        {
            // Decodificar URL por si el no. de parte tiene simbolos especiales
            var decoded = System.Net.WebUtility.UrlDecode(partNumber);

            var info = _lookupSvc.GetProductInfo(decoded);

            if (info == null)
                return NotFound(new { message = "Número de parte no encontrado" });

            return Ok(info);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
