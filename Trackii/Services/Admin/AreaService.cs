using MySql.Data.MySqlClient;
using Trackii.Models.Admin.Area;

namespace Trackii.Services.Admin;

public class AreaService
{
    private readonly string _conn;

    public AreaService(IConfiguration cfg)
    {
        _conn = cfg.GetConnectionString("TrackiiDb")!;
    }

    public AreaListVm GetPaged(string? search, bool showInactive, int page, int pageSize)
    {
        var vm = new AreaListVm
        {
            Search = search,
            ShowInactive = showInactive,
            Page = page,
            PageSize = pageSize
        };

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        var where = "WHERE 1=1 ";
        if (!string.IsNullOrWhiteSpace(search))
            where += "AND name LIKE @search ";
        if (!showInactive)
            where += "AND active = 1 ";

        using (var cmd = new MySqlCommand($"SELECT COUNT(*) FROM area {where}", cn))
        {
            if (!string.IsNullOrWhiteSpace(search))
                cmd.Parameters.AddWithValue("@search", $"%{search}%");
            vm.TotalRows = Convert.ToInt32(cmd.ExecuteScalar());
        }

        var offset = (page - 1) * pageSize;

        using (var cmd = new MySqlCommand($@"
            SELECT id, name, active
            FROM area
            {where}
            ORDER BY name
            LIMIT @ps OFFSET @off", cn))
        {
            if (!string.IsNullOrWhiteSpace(search))
                cmd.Parameters.AddWithValue("@search", $"%{search}%");
            cmd.Parameters.AddWithValue("@ps", pageSize);
            cmd.Parameters.AddWithValue("@off", offset);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                vm.Items.Add(new AreaRowVm
                {
                    Id = rd.GetUInt32("id"),
                    Name = rd.GetString("name"),
                    Active = rd.GetBoolean("active")
                });
            }
        }

        return vm;
    }

    public AreaEditVm? GetById(uint id)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(
            "SELECT id, name, active FROM area WHERE id = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);

        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;

        return new AreaEditVm
        {
            Id = rd.GetUInt32("id"),
            Name = rd.GetString("name"),
            Active = rd.GetBoolean("active")
        };
    }

    // MÉTODO NECESARIO PARA VALIDAR DUPLICADOS
    public bool Exists(string name, uint? id = null)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();
        var query = "SELECT COUNT(*) FROM area WHERE name = @name";
        if (id.HasValue) query += " AND id != @id";

        using var cmd = new MySqlCommand(query, cn);
        cmd.Parameters.AddWithValue("@name", name);
        if (id.HasValue) cmd.Parameters.AddWithValue("@id", id.Value);

        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public void Create(AreaEditVm vm)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(
            "INSERT INTO area (name, active) VALUES (@n, 1)", cn);
        cmd.Parameters.AddWithValue("@n", vm.Name);
        cmd.ExecuteNonQuery();
    }

    public void Update(AreaEditVm vm)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(
            "UPDATE area SET name = @n WHERE id = @id", cn);
        cmd.Parameters.AddWithValue("@n", vm.Name);
        cmd.Parameters.AddWithValue("@id", vm.Id);
        cmd.ExecuteNonQuery();
    }

    // CASCADA: Si desactivas el área, se apagan familias, subfamilias y productos
    // ==========================================
    // SET ACTIVE (Cascada Bidireccional)
    // ==========================================
    public void SetActive(uint id, bool active)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();
        using var tx = cn.BeginTransaction();

        // 1. Actualizar el Área
        using (var cmd = new MySqlCommand(
            "UPDATE area SET active = @a WHERE id = @id", cn, tx))
        {
            cmd.Parameters.AddWithValue("@a", active);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        // 2. CASCADA: Aplicar el mismo estado (1 o 0) a toda la descendencia
        var val = active ? 1 : 0;

        // A. Actualizar Familias
        using (var cmd = new MySqlCommand(
            "UPDATE family SET active = @val WHERE id_area = @id", cn, tx))
        {
            cmd.Parameters.AddWithValue("@val", val);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        // B. Actualizar Subfamilias (Join con Family)
        using (var cmd = new MySqlCommand(@"
            UPDATE subfamily s 
            JOIN family f ON f.id = s.id_family 
            SET s.active = @val 
            WHERE f.id_area = @id", cn, tx))
        {
            cmd.Parameters.AddWithValue("@val", val);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        // C. Actualizar Productos (Join con Family -> Subfamily)
        using (var cmd = new MySqlCommand(@"
            UPDATE product p
            JOIN subfamily s ON s.id = p.id_subfamily
            JOIN family f ON f.id = s.id_family
            SET p.active = @val
            WHERE f.id_area = @id", cn, tx))
        {
            cmd.Parameters.AddWithValue("@val", val);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        // D. Actualizar Rutas (Agregado para consistencia completa)
        using (var cmd = new MySqlCommand(@"
            UPDATE route r
            JOIN subfamily s ON s.id = r.subfamily_id
            JOIN family f ON f.id = s.id_family
            SET r.active = @val
            WHERE f.id_area = @id", cn, tx))
        {
            cmd.Parameters.AddWithValue("@val", val);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }
}