using MySql.Data.MySqlClient;
using Trackii.Models.Admin.Location;

namespace Trackii.Services.Admin;

public class LocationService
{
    private readonly string _conn;

    public LocationService(IConfiguration cfg)
    {
        _conn = cfg.GetConnectionString("TrackiiDb")
            ?? throw new Exception("Connection string TrackiiDb no configurada");
    }

    // ===================== LISTADO =====================
    public LocationListVm GetPaged(
        string? search,
        bool showInactive,
        int page,
        int pageSize)
    {
        var vm = new LocationListVm
        {
            Search = search,
            ShowInactive = showInactive,
            Page = page
        };

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        var where = " WHERE 1=1 ";
        if (!showInactive) where += " AND active = 1 ";
        if (!string.IsNullOrWhiteSpace(search))
            where += " AND name LIKE @search ";

        using var countCmd = new MySqlCommand(
            $"SELECT COUNT(*) FROM location {where}", cn);

        if (!string.IsNullOrWhiteSpace(search))
            countCmd.Parameters.AddWithValue("@search", $"%{search}%");

        var total = Convert.ToInt32(countCmd.ExecuteScalar());
        vm.TotalPages = (int)Math.Ceiling(total / (double)pageSize);

        using var cmd = new MySqlCommand($@"
            SELECT id, name, active
            FROM location
            {where}
            ORDER BY name
            LIMIT @off,@lim", cn);

        if (!string.IsNullOrWhiteSpace(search))
            cmd.Parameters.AddWithValue("@search", $"%{search}%");

        cmd.Parameters.AddWithValue("@off", (page - 1) * pageSize);
        cmd.Parameters.AddWithValue("@lim", pageSize);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            vm.Items.Add(new LocationListVm.Row
            {
                Id = rd.GetUInt32("id"),
                Name = rd.GetString("name"),
                Active = rd.GetBoolean("active")
            });
        }

        return vm;
    }

    // ===================== CRUD =====================
    public LocationEditVm? GetById(uint id)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(
            "SELECT id,name FROM location WHERE id=@id", cn);

        cmd.Parameters.AddWithValue("@id", id);
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;

        return new LocationEditVm
        {
            Id = id,
            Name = rd.GetString("name")
        };
    }

    public void Create(LocationEditVm vm)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(
            "INSERT INTO location (name, active) VALUES (@n,1)", cn);

        cmd.Parameters.AddWithValue("@n", vm.Name);
        cmd.ExecuteNonQuery();
    }

    public void Update(LocationEditVm vm)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(
            "UPDATE location SET name=@n WHERE id=@id", cn);

        cmd.Parameters.AddWithValue("@id", vm.Id);
        cmd.Parameters.AddWithValue("@n", vm.Name);
        cmd.ExecuteNonQuery();
    }

    // ===================== TOGGLE =====================
    public bool SetActive(uint id, bool active)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        if (!active)
        {
            using var chk = new MySqlCommand(@"
                SELECT
                  (SELECT COUNT(*) FROM route_step WHERE location_id=@id) +
                  (SELECT COUNT(*) FROM devices WHERE location_id=@id)", cn);

            chk.Parameters.AddWithValue("@id", id);
            if (Convert.ToInt32(chk.ExecuteScalar()) > 0)
                return false;
        }

        using var cmd = new MySqlCommand(
            "UPDATE location SET active=@a WHERE id=@id", cn);

        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@a", active);
        cmd.ExecuteNonQuery();
        return true;
    }
  

    // VALIDACIÓN DE DUPLICADOS
    public bool Exists(string name, uint? id = null)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        var query = "SELECT COUNT(*) FROM location WHERE name = @n";

        if (id.HasValue)
            query += " AND id != @id";

        using var cmd = new MySqlCommand(query, cn);
        cmd.Parameters.AddWithValue("@n", name);
        if (id.HasValue)
            cmd.Parameters.AddWithValue("@id", id.Value);

        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }
}