using MySql.Data.MySqlClient;
using Trackii.Models.Admin.HardMod;

namespace Trackii.Services.Admin;

public class HardModService
{
    private readonly string _conn;

    public HardModService(IConfiguration cfg)
    {
        _conn = cfg.GetConnectionString("TrackiiDb")
            ?? throw new Exception("Connection string TrackiiDb no configurada");
    }

    public HardModVm GetViewModel(uint? wipItemId = null, uint? wipStepExecutionId = null)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        return new HardModVm
        {
            WipItemPreview = wipItemId.HasValue ? GetWipItemPreview(cn, wipItemId.Value) : null,
            WipStepExecutionPreview = wipStepExecutionId.HasValue ? GetWipStepExecutionPreview(cn, wipStepExecutionId.Value) : null
        };
    }

    public HardDeleteResultVm HardDeleteWipItem(uint id)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();
        using var tx = cn.BeginTransaction();

        try
        {
            var preview = GetWipItemPreview(cn, id, tx);
            if (preview == null)
            {
                tx.Rollback();
                return new HardDeleteResultVm
                {
                    Success = false,
                    Message = $"No se encontró el wip_item #{id}."
                };
            }

            ExecuteDelete(cn, tx, "DELETE FROM wip_step_execution WHERE wip_item_id=@id", id);
            ExecuteDelete(cn, tx, "DELETE FROM scan_event WHERE wip_item_id=@id", id);
            ExecuteDelete(cn, tx, "DELETE FROM wip_rework_log WHERE wip_item_id=@id", id);
            var deleted = ExecuteDelete(cn, tx, "DELETE FROM wip_item WHERE id=@id", id);

            if (deleted == 0)
            {
                tx.Rollback();
                return new HardDeleteResultVm
                {
                    Success = false,
                    Message = $"No fue posible eliminar el wip_item #{id}."
                };
            }

            tx.Commit();
            return new HardDeleteResultVm
            {
                Success = true,
                Message = $"WIP item #{id} eliminado de la BD junto con {preview.StepExecutionsCount} step execution(s), {preview.ScanEventsCount} scan event(s) y {preview.ReworkLogsCount} rework log(s)."
            };
        }
        catch (Exception ex)
        {
            tx.Rollback();
            return new HardDeleteResultVm
            {
                Success = false,
                Message = $"Error al eliminar el wip_item #{id}: {ex.Message}"
            };
        }
    }

    public HardDeleteResultVm HardDeleteWipStepExecution(uint id)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        try
        {
            using var cmd = new MySqlCommand("DELETE FROM wip_step_execution WHERE id=@id", cn);
            cmd.Parameters.AddWithValue("@id", id);
            var deleted = cmd.ExecuteNonQuery();

            return deleted > 0
                ? new HardDeleteResultVm
                {
                    Success = true,
                    Message = $"WIP step execution #{id} eliminado de la BD."
                }
                : new HardDeleteResultVm
                {
                    Success = false,
                    Message = $"No se encontró el wip_step_execution #{id}."
                };
        }
        catch (Exception ex)
        {
            return new HardDeleteResultVm
            {
                Success = false,
                Message = $"Error al eliminar el wip_step_execution #{id}: {ex.Message}"
            };
        }
    }

    private static int ExecuteDelete(MySqlConnection cn, MySqlTransaction tx, string sql, uint id)
    {
        using var cmd = new MySqlCommand(sql, cn, tx);
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery();
    }

    private static WipItemPreviewVm? GetWipItemPreview(MySqlConnection cn, uint id, MySqlTransaction? tx = null)
    {
        using var cmd = new MySqlCommand(@"
            SELECT
                wip.id,
                wip.status,
                wip.created_at,
                wo.wo_number,
                p.part_number,
                l.name AS current_location,
                (SELECT COUNT(*) FROM wip_step_execution WHERE wip_item_id = wip.id) AS step_execs,
                (SELECT COUNT(*) FROM scan_event WHERE wip_item_id = wip.id) AS scan_events,
                (SELECT COUNT(*) FROM wip_rework_log WHERE wip_item_id = wip.id) AS rework_logs
            FROM wip_item wip
            JOIN work_order wo ON wo.id = wip.wo_order_id
            JOIN product p ON p.id = wo.product_id
            LEFT JOIN route_step rs ON rs.id = wip.current_step_id
            LEFT JOIN location l ON l.id = rs.location_id
            WHERE wip.id = @id
            LIMIT 1;", cn, tx);

        cmd.Parameters.AddWithValue("@id", id);
        using var rd = cmd.ExecuteReader();
        if (!rd.Read())
            return null;

        var currentLocationOrdinal = rd.GetOrdinal("current_location");
        var createdAtOrdinal = rd.GetOrdinal("created_at");

        return new WipItemPreviewVm
        {
            Id = rd.GetUInt32("id"),
            Status = rd.GetString("status"),
            WorkOrderNumber = rd.GetString("wo_number"),
            ProductPartNumber = rd.GetString("part_number"),
            CurrentLocation = rd.IsDBNull(currentLocationOrdinal) ? null : rd.GetString("current_location"),
            CreatedAt = rd.IsDBNull(createdAtOrdinal) ? null : rd.GetDateTime(createdAtOrdinal),
            StepExecutionsCount = Convert.ToInt32(rd["step_execs"]),
            ScanEventsCount = Convert.ToInt32(rd["scan_events"]),
            ReworkLogsCount = Convert.ToInt32(rd["rework_logs"]),
        };
    }

    private static WipStepExecutionPreviewVm? GetWipStepExecutionPreview(MySqlConnection cn, uint id, MySqlTransaction? tx = null)
    {
        using var cmd = new MySqlCommand(@"
            SELECT
                wse.id,
                wse.wip_item_id,
                wse.create_at,
                wse.qty_in,
                wse.qty_scrap,
                l.name AS location_name,
                wo.wo_number,
                p.part_number
            FROM wip_step_execution wse
            JOIN wip_item wip ON wip.id = wse.wip_item_id
            JOIN work_order wo ON wo.id = wip.wo_order_id
            JOIN product p ON p.id = wo.product_id
            JOIN location l ON l.id = wse.location_id
            WHERE wse.id = @id
            LIMIT 1;", cn, tx);

        cmd.Parameters.AddWithValue("@id", id);
        using var rd = cmd.ExecuteReader();
        if (!rd.Read())
            return null;

        var createdAtOrdinal = rd.GetOrdinal("create_at");

        return new WipStepExecutionPreviewVm
        {
            Id = rd.GetUInt32("id"),
            WipItemId = rd.GetUInt32("wip_item_id"),
            WorkOrderNumber = rd.GetString("wo_number"),
            ProductPartNumber = rd.GetString("part_number"),
            LocationName = rd.GetString("location_name"),
            CreatedAt = rd.IsDBNull(createdAtOrdinal) ? null : rd.GetDateTime(createdAtOrdinal),
            QtyIn = Convert.ToUInt32(rd["qty_in"]),
            QtyScrap = Convert.ToUInt32(rd["qty_scrap"])
        };
    }
}
