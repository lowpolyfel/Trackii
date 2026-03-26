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

    public HardModVm GetViewModel(string? search = null)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        var vm = new HardModVm { Search = search?.Trim() };
        if (string.IsNullOrWhiteSpace(vm.Search))
            return vm;

        var results = SearchWorkOrders(cn, vm.Search);
        vm.SearchMessage = results.Count == 0
            ? $"No se encontraron coincidencias para '{vm.Search}'."
            : $"Se encontraron {results.Count} resultado(s) para '{vm.Search}'.";

        foreach (var item in results)
            vm.Results.Add(item);

        return vm;
    }

    public HardDeleteResultVm HardDeleteByWorkOrder(uint workOrderId)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();
        using var tx = cn.BeginTransaction();

        try
        {
            var preview = GetDeletePreview(cn, tx, workOrderId);
            if (preview == null)
            {
                tx.Rollback();
                return new HardDeleteResultVm
                {
                    Success = false,
                Message = $"No se encontró el work_order #{workOrderId}."
                };
            }

            var dependencyDeletes = DeleteReferencingRows(cn, tx, "work_order", "id", workOrderId);
            var deletedWorkOrders = ExecuteDelete(cn, tx, "DELETE FROM work_order WHERE id=@id", workOrderId);
            if (deletedWorkOrders == 0)
            {
                tx.Rollback();
                return new HardDeleteResultVm
                {
                    Success = false,
                    Message = $"No fue posible eliminar el work_order #{workOrderId}."
                };
            }

            tx.Commit();
            return new HardDeleteResultVm
            {
                Success = true,
                Message = $"Eliminado WO #{preview.WorkOrderId} ({preview.WorkOrderNumber}) con {preview.WipItemsCount} WIP item(s), {preview.WipStepExecutionsCount} WIP step execution(s) y {dependencyDeletes} registro(s) dependiente(s)."
            };
        }
        catch (Exception ex)
        {
            tx.Rollback();
            return new HardDeleteResultVm
            {
                Success = false,
                Message = $"Error al eliminar WO #{workOrderId}: {ex.Message}"
            };
        }
    }

    private static List<HardModSearchResultVm> SearchWorkOrders(MySqlConnection cn, string search)
    {
        var results = new List<HardModSearchResultVm>();

        using var cmd = new MySqlCommand(@"
            SELECT
                wo.id AS work_order_id,
                wo.wo_number,
                p.part_number,
                (SELECT COUNT(*) FROM wip_item wi WHERE wi.wo_order_id = wo.id) AS wip_items_count,
                (SELECT COUNT(*)
                   FROM wip_step_execution wse
                   JOIN wip_item wi2 ON wi2.id = wse.wip_item_id
                  WHERE wi2.wo_order_id = wo.id) AS wip_step_executions_count,
                (SELECT GROUP_CONCAT(wi3.id ORDER BY wi3.id SEPARATOR ', ')
                   FROM wip_item wi3
                  WHERE wi3.wo_order_id = wo.id) AS wip_item_ids_preview
            FROM work_order wo
            JOIN product p ON p.id = wo.product_id
            WHERE wo.wo_number LIKE @likeSearch
               OR p.part_number LIKE @likeSearch
               OR CAST(wo.id AS CHAR) = @exactSearch
               OR EXISTS (
                    SELECT 1 FROM wip_item wi
                    WHERE wi.wo_order_id = wo.id
                      AND CAST(wi.id AS CHAR) = @exactSearch
               )
               OR EXISTS (
                    SELECT 1
                    FROM wip_step_execution wse
                    JOIN wip_item wi ON wi.id = wse.wip_item_id
                    WHERE wi.wo_order_id = wo.id
                      AND CAST(wse.id AS CHAR) = @exactSearch
               )
            ORDER BY wo.id DESC
            LIMIT 150;", cn);

        cmd.Parameters.AddWithValue("@likeSearch", $"%{search}%");
        cmd.Parameters.AddWithValue("@exactSearch", search);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var wipItemsCount = Convert.ToInt32(rd["wip_items_count"]);
            var wipStepExecutionsCount = Convert.ToInt32(rd["wip_step_executions_count"]);
            var wipIdsOrdinal = rd.GetOrdinal("wip_item_ids_preview");

            results.Add(new HardModSearchResultVm
            {
                WorkOrderId = rd.GetUInt32("work_order_id"),
                WorkOrderNumber = rd.GetString("wo_number"),
                ProductPartNumber = rd.GetString("part_number"),
                WipItemsCount = wipItemsCount,
                WipStepExecutionsCount = wipStepExecutionsCount,
                WipItemIdsPreview = rd.IsDBNull(wipIdsOrdinal) ? "-" : rd.GetString(wipIdsOrdinal),
                ExistsInWorkOrder = true,
                ExistsInWipItem = wipItemsCount > 0,
                ExistsInWipStepExecution = wipStepExecutionsCount > 0
            });
        }

        return results;
    }

    private static HardDeletePreviewVm? GetDeletePreview(
        MySqlConnection cn,
        MySqlTransaction tx,
        uint workOrderId)
    {
        using var cmd = new MySqlCommand(@"
            SELECT
                wo.id AS work_order_id,
                wo.wo_number,
                (SELECT COUNT(*) FROM wip_item wi WHERE wi.wo_order_id = wo.id) AS wip_items_count,
                (SELECT COUNT(*)
                   FROM wip_step_execution wse
                   JOIN wip_item wi2 ON wi2.id = wse.wip_item_id
                  WHERE wi2.wo_order_id = wo.id) AS wip_step_executions_count
            FROM work_order wo
            WHERE wo.id = @id
            LIMIT 1;", cn, tx);

        cmd.Parameters.AddWithValue("@id", workOrderId);
        using var rd = cmd.ExecuteReader();
        if (!rd.Read())
            return null;

        return new HardDeletePreviewVm
        {
            WorkOrderId = rd.GetUInt32("work_order_id"),
            WorkOrderNumber = rd.GetString("wo_number"),
            WipItemsCount = Convert.ToInt32(rd["wip_items_count"]),
            WipStepExecutionsCount = Convert.ToInt32(rd["wip_step_executions_count"])
        };
    }

    private static int ExecuteDelete(MySqlConnection cn, MySqlTransaction tx, string sql, uint id)
    {
        using var cmd = new MySqlCommand(sql, cn, tx);
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery();
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

    private class HardDeletePreviewVm
    {
        public uint WorkOrderId { get; set; }
        public string WorkOrderNumber { get; set; } = string.Empty;
        public int WipItemsCount { get; set; }
        public int WipStepExecutionsCount { get; set; }
    }
}
