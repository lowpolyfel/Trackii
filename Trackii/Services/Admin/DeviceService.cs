using MySql.Data.MySqlClient;
using System.Data;
using Trackii.Models.Admin.Device;

namespace Trackii.Services.Admin;

public class DeviceService
{
    private readonly string _conn;

    public DeviceService(IConfiguration cfg)
    {
        _conn = cfg.GetConnectionString("TrackiiDb")
            ?? throw new Exception("Connection string TrackiiDb no configurada");
    }

    public DeviceListVm GetPaged(string? search, bool showInactive, int page, int pageSize)
    {
        var vm = new DeviceListVm
        {
            Search = search,
            ShowInactive = showInactive,
            Page = page
        };

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        var where = " WHERE 1=1 ";
        if (!showInactive) where += " AND d.active = 1 ";
        if (!string.IsNullOrWhiteSpace(search))
            where += " AND (d.device_uid LIKE @search OR d.name LIKE @search) ";

        using var countCmd = new MySqlCommand($@"
            SELECT COUNT(*)
            FROM devices d
            {where}", cn);

        if (!string.IsNullOrWhiteSpace(search))
            countCmd.Parameters.AddWithValue("@search", $"%{search}%");

        var total = Convert.ToInt32(countCmd.ExecuteScalar());
        vm.TotalPages = (int)Math.Ceiling(total / (double)pageSize);

        using var cmd = new MySqlCommand($@"
            SELECT d.id, d.device_uid, d.name, d.active, l.name AS location_name
            FROM devices d
            JOIN location l ON l.id = d.location_id
            {where}
            ORDER BY d.device_uid
            LIMIT @off,@lim", cn);

        if (!string.IsNullOrWhiteSpace(search))
            cmd.Parameters.AddWithValue("@search", $"%{search}%");

        cmd.Parameters.AddWithValue("@off", (page - 1) * pageSize);
        cmd.Parameters.AddWithValue("@lim", pageSize);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            vm.Items.Add(new DeviceListVm.Row
            {
                Id = rd.GetUInt32("id"),
                DeviceUid = rd.GetString("device_uid"),
                Name = rd.IsDBNull("name") ? "" : rd.GetString("name"),
                LocationName = rd.GetString("location_name"),
                Active = rd.GetBoolean("active")
            });
        }

        return vm;
    }

    public DeviceEditVm? GetById(uint id)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(@"
            SELECT id, device_uid, name, location_id
            FROM devices
            WHERE id=@id", cn);

        cmd.Parameters.AddWithValue("@id", id);
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;

        return new DeviceEditVm
        {
            Id = id,
            DeviceUid = rd.GetString("device_uid"),
            Name = rd.IsDBNull("name") ? null : rd.GetString("name"),
            LocationId = rd.GetUInt32("location_id")
        };
    }

    public void Create(DeviceEditVm vm)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(@"
            INSERT INTO devices (device_uid, location_id, name, active)
            VALUES (@uid, @loc, @name, 1)", cn);

        cmd.Parameters.AddWithValue("@uid", vm.DeviceUid);
        cmd.Parameters.AddWithValue("@loc", vm.LocationId);
        cmd.Parameters.AddWithValue("@name", string.IsNullOrWhiteSpace(vm.Name) ? DBNull.Value : vm.Name);
        cmd.ExecuteNonQuery();
    }

    public void Update(DeviceEditVm vm)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(@"
            UPDATE devices
            SET device_uid=@uid,
                location_id=@loc,
                name=@name
            WHERE id=@id", cn);

        cmd.Parameters.AddWithValue("@id", vm.Id);
        cmd.Parameters.AddWithValue("@uid", vm.DeviceUid);
        cmd.Parameters.AddWithValue("@loc", vm.LocationId);
        cmd.Parameters.AddWithValue("@name", string.IsNullOrWhiteSpace(vm.Name) ? DBNull.Value : vm.Name);
        cmd.ExecuteNonQuery();
    }

    public void SetActive(uint id, bool active)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand("UPDATE devices SET active=@a WHERE id=@id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@a", active);
        cmd.ExecuteNonQuery();
    }

    public bool ExistsDeviceUid(string deviceUid, uint? id = null)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        var query = "SELECT COUNT(*) FROM devices WHERE device_uid = @uid";
        if (id.HasValue) query += " AND id != @id";

        using var cmd = new MySqlCommand(query, cn);
        cmd.Parameters.AddWithValue("@uid", deviceUid);
        if (id.HasValue) cmd.Parameters.AddWithValue("@id", id.Value);

        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public List<(uint Id, string Name)> GetActiveLocations()
    {
        var list = new List<(uint, string)>();

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(
            "SELECT id, name FROM location WHERE active = 1 ORDER BY name", cn);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            list.Add((rd.GetUInt32("id"), rd.GetString("name")));

        return list;
    }
}
