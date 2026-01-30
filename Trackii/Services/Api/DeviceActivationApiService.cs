using MySql.Data.MySqlClient;
using Trackii.Models.Api;

namespace Trackii.Services.Api;

public class DeviceActivationApiService
{
    private readonly string _conn;

    public DeviceActivationApiService(IConfiguration cfg)
    {
        _conn = cfg.GetConnectionString("TrackiiDb")
            ?? throw new Exception("Connection string TrackiiDb no configurada");
    }

    public DeviceActivationResponse Activate(string token, string androidId)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();
        using var tx = cn.BeginTransaction();

        try
        {
            // 1) Validar token
            using (var chk = new MySqlCommand(
                "SELECT id FROM tokens WHERE code=@t LIMIT 1", cn, tx))
            {
                chk.Parameters.AddWithValue("@t", token);
                if (chk.ExecuteScalar() == null)
                {
                    tx.Rollback();
                    return new DeviceActivationResponse
                    {
                        Ok = false,
                        Reason = "INVALID_TOKEN"
                    };
                }
            }

            // 2) Upsert device por androidId
            uint deviceId;

            using (var q = new MySqlCommand(@"
                SELECT id
                FROM devices
                WHERE device_uid=@uid
                LIMIT 1
                FOR UPDATE", cn, tx))
            {
                q.Parameters.AddWithValue("@uid", androidId);
                var obj = q.ExecuteScalar();

                if (obj != null)
                {
                    deviceId = Convert.ToUInt32(obj);

                    using var up = new MySqlCommand(@"
                        UPDATE devices
                        SET active=1, location_id=NULL
                        WHERE id=@id", cn, tx);
                    up.Parameters.AddWithValue("@id", deviceId);
                    up.ExecuteNonQuery();
                }
                else
                {
                    using var ins = new MySqlCommand(@"
                        INSERT INTO devices (device_uid, location_id, active)
                        VALUES (@uid, NULL, 1)", cn, tx);
                    ins.Parameters.AddWithValue("@uid", androidId);
                    ins.ExecuteNonQuery();
                    deviceId = Convert.ToUInt32(ins.LastInsertedId);
                }
            }

            tx.Commit();
            return new DeviceActivationResponse
            {
                Ok = true,
                DeviceId = deviceId,
                DeviceUid = androidId
            };
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
