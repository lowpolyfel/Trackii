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

    public HardModVm GetViewModel(string? wipItemLookup = null, uint? wipStepExecutionId = null)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        uint? resolvedWipItemId = null;
        string? lookupMessage = null;
        if (!string.IsNullOrWhiteSpace(wipItemLookup))
        {
            resolvedWipItemId = ResolveWipItemId(cn, wipItemLookup, out lookupMessage);
        }

        return new HardModVm
        {
            WipItemLookup = wipItemLookup,
            WipItemLookupMessage = lookupMessage,
            WipItemPreview = resolvedWipItemId.HasValue ? GetWipItemPreview(cn, resolvedWipItemId.Value) : null,
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

            var fkDeletes = DeleteReferencingRows(cn, tx, "wip_item", "id", id);
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
                    Message = $"WIP item #{id} eliminado de la BD junto con {preview.StepExecutionsCount} step execution(s), {preview.ScanEventsCount} scan event(s), {preview.ReworkLogsCount} rework log(s) y {fkDeletes} registro(s) dependiente(s) totales."
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

    private static uint? ResolveWipItemId(MySqlConnection cn, string lookup, out string? message)
    {
        message = null;
        var trimmed = lookup.Trim();
        if (uint.TryParse(trimmed, out var wipItemId))
        {
            message = $"Búsqueda por ID de WIP item: {wipItemId}.";
            return wipItemId;
        }

        using var cmd = new MySqlCommand(@"
            SELECT wip.id
            FROM wip_item wip
            JOIN work_order wo ON wo.id = wip.wo_order_id
            WHERE wo.wo_number = @woNumber
            ORDER BY wip.created_at DESC, wip.id DESC
            LIMIT 1;", cn);

        cmd.Parameters.AddWithValue("@woNumber", trimmed);
        var result = cmd.ExecuteScalar();
        if (result == null)
        {
            message = $"No se encontró WIP item para el No. de orden '{trimmed}'.";
            return null;
        }

        var resolvedId = Convert.ToUInt32(result);
        message = $"Búsqueda por No. de orden '{trimmed}': se cargó el WIP item más reciente #{resolvedId}.";
        return resolvedId;
    }

    private static int DeleteReferencingRows(
        MySqlConnection cn,
        MySqlTransaction tx,
        string referencedTable,
        string referencedColumn,
        uint id)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return DeleteReferencingRowsRecursive(cn, tx, referencedTable, referencedColumn, id, visited);
    }

    private static int DeleteReferencingRowsRecursive(
        MySqlConnection cn,
        MySqlTransaction tx,
        string referencedTable,
        string referencedColumn,
        uint id,
        HashSet<string> visited)
    {
        var visitKey = $"{referencedTable}:{referencedColumn}:{id}";
        if (!visited.Add(visitKey))
            return 0;

        var foreignKeys = GetReferencingForeignKeys(cn, tx, referencedTable, referencedColumn);
        var totalDeleted = 0;

        foreach (var fk in foreignKeys)
        {
            var primaryKeyColumn = GetPrimaryKeyColumn(cn, tx, fk.TableName);
            if (!string.IsNullOrWhiteSpace(primaryKeyColumn))
            {
                foreach (var childId in GetChildPrimaryIds(cn, tx, fk.TableName, fk.ColumnName, primaryKeyColumn, id))
                {
                    totalDeleted += DeleteReferencingRowsRecursive(
                        cn,
                        tx,
                        fk.TableName,
                        primaryKeyColumn,
                        childId,
                        visited);
                }
            }

            var sql = $"DELETE FROM `{fk.TableName}` WHERE `{fk.ColumnName}` = @id";
            using var cmd = new MySqlCommand(sql, cn, tx);
            cmd.Parameters.AddWithValue("@id", id);
            totalDeleted += cmd.ExecuteNonQuery();
        }

        return totalDeleted;
    }

    private static List<(string TableName, string ColumnName)> GetReferencingForeignKeys(
        MySqlConnection cn,
        MySqlTransaction tx,
        string referencedTable,
        string referencedColumn)
    {
        var refs = new List<(string TableName, string ColumnName)>();

        using var cmd = new MySqlCommand(@"
            SELECT DISTINCT kcu.TABLE_NAME, kcu.COLUMN_NAME
            FROM information_schema.KEY_COLUMN_USAGE kcu
            WHERE kcu.REFERENCED_TABLE_SCHEMA = DATABASE()
              AND kcu.REFERENCED_TABLE_NAME = @referencedTable
              AND kcu.REFERENCED_COLUMN_NAME = @referencedColumn
              AND kcu.TABLE_NAME <> @referencedTable;", cn, tx);

        cmd.Parameters.AddWithValue("@referencedTable", referencedTable);
        cmd.Parameters.AddWithValue("@referencedColumn", referencedColumn);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            refs.Add((rd.GetString("TABLE_NAME"), rd.GetString("COLUMN_NAME")));
        }

        return refs;
    }

    private static string? GetPrimaryKeyColumn(MySqlConnection cn, MySqlTransaction tx, string tableName)
    {
        using var cmd = new MySqlCommand(@"
            SELECT kcu.COLUMN_NAME
            FROM information_schema.TABLE_CONSTRAINTS tc
            JOIN information_schema.KEY_COLUMN_USAGE kcu
              ON tc.CONSTRAINT_SCHEMA = kcu.CONSTRAINT_SCHEMA
             AND tc.TABLE_NAME = kcu.TABLE_NAME
             AND tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
            WHERE tc.CONSTRAINT_SCHEMA = DATABASE()
              AND tc.TABLE_NAME = @tableName
              AND tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
            ORDER BY kcu.ORDINAL_POSITION
            LIMIT 1;", cn, tx);

        cmd.Parameters.AddWithValue("@tableName", tableName);
        var result = cmd.ExecuteScalar();
        return result?.ToString();
    }

    private static List<uint> GetChildPrimaryIds(
        MySqlConnection cn,
        MySqlTransaction tx,
        string tableName,
        string fkColumnName,
        string pkColumnName,
        uint parentId)
    {
        var ids = new List<uint>();
        var sql = $@"
            SELECT DISTINCT `{pkColumnName}`
            FROM `{tableName}`
            WHERE `{fkColumnName}` = @parentId";

        using var cmd = new MySqlCommand(sql, cn, tx);
        cmd.Parameters.AddWithValue("@parentId", parentId);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            if (!rd.IsDBNull(0))
            {
                ids.Add(Convert.ToUInt32(rd.GetValue(0)));
            }
        }

        return ids;
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
