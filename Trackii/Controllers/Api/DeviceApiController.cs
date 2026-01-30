using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using Trackii.Models.Api;

namespace Trackii.Controllers.Api;

[ApiController]
[Route("api")]
public class DeviceApiController : ControllerBase
{
    private readonly IConfiguration _config;

    public DeviceApiController(IConfiguration config)
    {
        _config = config;
    }

    private MySqlConnection GetConn()
        => new MySqlConnection(_config.GetConnectionString("TrackiiDb"));

    // ==========================
    // GET: api/locations
    // ==========================
    [HttpGet("locations")]
    public IActionResult GetLocations()
    {
        var list = new List<LocationDto>();

        using var conn = GetConn();
        conn.Open();

        var cmd = new MySqlCommand(
            "SELECT id, name FROM location WHERE active = 1 ORDER BY name",
            conn);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            list.Add(new LocationDto
            {
                Id = rd.GetUInt32("id"),
                Name = rd.GetString("name")
            });
        }

        return Ok(list);
    }

    // ==========================
    // POST: api/devices/register
    // ==========================
    [HttpPost("devices/register")]
    public IActionResult RegisterDevice([FromBody] RegisterDeviceRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.DeviceUid))
            return BadRequest("DeviceUid requerido");

        using var conn = GetConn();
        conn.Open();

        // ¿Ya existe?
        var check = new MySqlCommand(
            "SELECT id FROM devices WHERE device_uid = @uid",
            conn);
        check.Parameters.AddWithValue("@uid", req.DeviceUid);

        var exists = check.ExecuteScalar();
        if (exists != null)
            return Conflict("Dispositivo ya registrado");

        var cmd = new MySqlCommand(@"
            INSERT INTO devices (device_uid, location_id, name, active)
            VALUES (@uid, @loc, @name, 1)", conn);

        cmd.Parameters.AddWithValue("@uid", req.DeviceUid);
        cmd.Parameters.AddWithValue("@loc", req.LocationId);
        cmd.Parameters.AddWithValue("@name", req.DeviceName);

        cmd.ExecuteNonQuery();

        return Ok();
    }
}
