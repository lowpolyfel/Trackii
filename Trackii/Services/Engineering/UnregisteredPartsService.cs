using MySql.Data.MySqlClient;
using Trackii.Models.Engineering;

namespace Trackii.Services.Engineering;

public class UnregisteredPartsService
{
    private readonly string _conn;

    public UnregisteredPartsService(IConfiguration cfg)
    {
        _conn = cfg.GetConnectionString("TrackiiDb")
            ?? throw new Exception("Connection string TrackiiDb no configurada");
    }

    public UnregisteredPartsListVm GetPaged(string? search, bool onlyActive, int page, int pageSize)
    {
        var vm = new UnregisteredPartsListVm
        {
            Search = search,
            OnlyActive = onlyActive,
            Page = page
        };

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        var where = " WHERE 1=1 ";
        if (onlyActive) where += " AND active = 1 ";
        if (!string.IsNullOrWhiteSpace(search)) where += " AND part_number LIKE @search ";

        using var countCmd = new MySqlCommand($"SELECT COUNT(*) FROM unregistered_parts {where}", cn);
        if (!string.IsNullOrWhiteSpace(search))
            countCmd.Parameters.AddWithValue("@search", $"%{search}%");

        var total = Convert.ToInt32(countCmd.ExecuteScalar());
        vm.TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));

        using var cmd = new MySqlCommand($@"
            SELECT part_id, part_number, creation_datetime, active
            FROM unregistered_parts
            {where}
            ORDER BY creation_datetime
            LIMIT @off,@lim", cn);

        if (!string.IsNullOrWhiteSpace(search))
            cmd.Parameters.AddWithValue("@search", $"%{search}%");

        cmd.Parameters.AddWithValue("@off", (page - 1) * pageSize);
        cmd.Parameters.AddWithValue("@lim", pageSize);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            vm.Items.Add(new UnregisteredPartsListVm.Row
            {
                PartId = rd.GetUInt32("part_id"),
                PartNumber = rd.GetString("part_number"),
                CreatedAt = rd.GetDateTime("creation_datetime"),
                Active = rd.GetBoolean("active")
            });
        }

        return vm;
    }

    public void Close(uint partId)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(
            "UPDATE unregistered_parts SET active = 0 WHERE part_id = @id", cn);
        cmd.Parameters.AddWithValue("@id", partId);
        cmd.ExecuteNonQuery();
    }

    public EngineeringLobbyVm GetLobbyData()
    {
        var vm = new EngineeringLobbyVm();

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var countCmd = new MySqlCommand(@"
            SELECT
                SUM(CASE WHEN active = 1 THEN 1 ELSE 0 END) AS active_count,
                COUNT(*) AS total_count
            FROM unregistered_parts", cn);

        using (var rd = countCmd.ExecuteReader())
        {
            if (rd.Read())
            {
                vm.ActiveUnregisteredCount = Convert.ToInt32(rd.GetInt64("active_count"));
                vm.TotalUnregisteredCount = Convert.ToInt32(rd.GetInt64("total_count"));
            }
        }

        using var oldestCmd = new MySqlCommand(@"
            SELECT part_id, part_number, creation_datetime,
                   DATEDIFF(NOW(), creation_datetime) AS age_days
            FROM unregistered_parts
            WHERE active = 1
            ORDER BY creation_datetime
            LIMIT 5", cn);

        using (var rd = oldestCmd.ExecuteReader())
        {
            while (rd.Read())
            {
                vm.OldestUnregistered.Add(new EngineeringLobbyVm.UnregisteredPartRow
                {
                    PartId = rd.GetUInt32("part_id"),
                    PartNumber = rd.GetString("part_number"),
                    CreatedAt = rd.GetDateTime("creation_datetime"),
                    AgeDays = Convert.ToInt32(rd.GetInt64("age_days"))
                });
            }
        }

        using var recentCmd = new MySqlCommand(@"
            SELECT part_id, part_number, creation_datetime,
                   DATEDIFF(NOW(), creation_datetime) AS age_days
            FROM unregistered_parts
            WHERE active = 1
            ORDER BY creation_datetime DESC
            LIMIT 5", cn);

        using var recentRd = recentCmd.ExecuteReader();
        while (recentRd.Read())
        {
            vm.RecentUnregistered.Add(new EngineeringLobbyVm.UnregisteredPartRow
            {
                PartId = recentRd.GetUInt32("part_id"),
                PartNumber = recentRd.GetString("part_number"),
                CreatedAt = recentRd.GetDateTime("creation_datetime"),
                AgeDays = Convert.ToInt32(recentRd.GetInt64("age_days"))
            });
        }

        return vm;
    }
}
