using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trackii.Models.Api;
using Trackii.Services.Api;

namespace Trackii.Controllers.Api;

[ApiController]
[Route("api/v1/device")]
public class DeviceApiController : ControllerBase
{
    private readonly DeviceActivationApiService _activation;
    private readonly DeviceApiService _device;

    public DeviceApiController(
        DeviceActivationApiService activation,
        DeviceApiService device)
    {
        _activation = activation;
        _device = device;
    }

    // 1) Activación por token (Dar de alta) - NO requiere login
    // POST /api/v1/device/activate
    [HttpPost("activate")]
    [AllowAnonymous]
    public ActionResult<DeviceActivationResponse> Activate(
        [FromBody] DeviceActivationRequest req)
    {
        if (req == null)
            return BadRequest("request requerido");

        if (string.IsNullOrWhiteSpace(req.Token))
            return BadRequest("token requerido");

        if (string.IsNullOrWhiteSpace(req.AndroidId))
            return BadRequest("androidId requerido");

        var result = _activation.Activate(
            req.Token.Trim(),
            req.AndroidId.Trim());

        if (!result.Ok)
            return Unauthorized(result.Reason);

        return Ok(result);
    }

    // 2) Bind de localidad (después de activar + login)
    // POST /api/v1/device/bind
    [HttpPost("bind")]
    [Authorize(AuthenticationSchemes = "ApiBearer")]
    public ActionResult<DeviceBindResponse> Bind(
        [FromBody] DeviceBindRequest req)
    {
        if (req == null)
            return BadRequest("request requerido");

        if (string.IsNullOrWhiteSpace(req.DeviceUid))
            return BadRequest("deviceUid requerido");

        if (req.LocationId <= 0)
            return BadRequest("locationId requerido");

        var (deviceId, locationId, locationName) =
            _device.Bind(req.DeviceUid.Trim(), req.LocationId);

        return Ok(new DeviceBindResponse
        {
            DeviceId = deviceId,
            LocationId = locationId,
            LocationName = locationName
        });
    }

    // 3) Status para header de la app (device + location)
    // GET /api/v1/device/{deviceId}
    [HttpGet("{deviceId:int}")]
    [Authorize(AuthenticationSchemes = "ApiBearer")]
    public ActionResult<DeviceStatusResponse> Get(int deviceId)
    {
        if (deviceId <= 0)
            return BadRequest("deviceId inválido");

        var info = _device.GetStatus((uint)deviceId);
        if (info == null)
            return NotFound();

        return Ok(info);
    }
}
