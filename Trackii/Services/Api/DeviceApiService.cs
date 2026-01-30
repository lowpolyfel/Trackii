using MySql.Data.MySqlClient;
using Trackii.Models.Api;

namespace Trackii.Services.Api;

public class DeviceApiService
{
    private readonly string _conn;

    public DeviceApiService(IConfiguration cfg)
    {
        _conn = cfg.GetConnectionString("TrackiiDb")
            ?? throw new Exception("Connection string TrackiiDb no configurada");
    }

    public (uint DeviceId, uint LocationId, string LocationName) Bind(string deviceUid, uint locationId)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        // Validar location activa
        string locName;
        using (var lc = new MySqlCommand("SELECT name FROM location WHERE id=@id AND active=1 LIMIT 1", cn))
        {
            lc.Parameters.AddWithValue("@id", locationId);
            locName = Convert.ToString(lc.ExecuteScalar()) ?? "";
            if (string.IsNullOrWhiteSpace(locName))
                throw new Exception("Location inválida o inactiva");
        }

        // Upsert device por device_uid
        using (var up = new MySqlCommand(@"
            INSERT INTO devices (device_uid, location_id, name, active)
            VALUES (@uid, @loc, NULL, 1)
            ON DUPLICATE KEY UPDATE location_id=@loc, active=1;", cn))
        {
            up.Parameters.AddWithValue("@uid", deviceUid);
            up.Parameters.AddWithValue("@loc", locationId);
            up.ExecuteNonQuery();
        }

        uint deviceId;
        using (var q = new MySqlCommand("SELECT id FROM devices WHERE device_uid=@uid LIMIT 1", cn))
        {
            q.Parameters.AddWithValue("@uid", deviceUid);
            deviceId = Convert.ToUInt32(q.ExecuteScalar());
        }

        return (deviceId, locationId, locName);
    }

    // Para el header: Device + Location (puede NO tener location aún)
    public DeviceStatusResponse? GetStatus(uint deviceId)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(@"
            SELECT d.id, d.device_uid, d.active, d.location_id, l.name AS location_name
            FROM devices d
            LEFT JOIN location l ON l.id = d.location_id
            WHERE d.id=@id
            LIMIT 1", cn);

        cmd.Parameters.AddWithValue("@id", deviceId);
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;

        uint? locId = rd.IsDBNull(rd.GetOrdinal("location_id")) ? null : rd.GetUInt32("location_id");
        string? locName = rd.IsDBNull(rd.GetOrdinal("location_name")) ? null : rd.GetString("location_name");

        return new DeviceStatusResponse
        {
            DeviceId = rd.GetUInt32("id"),
            DeviceUid = rd.GetString("device_uid"),
            Active = rd.GetBoolean("active"),
            LocationId = locId,
            LocationName = locName
        };
    }
}
