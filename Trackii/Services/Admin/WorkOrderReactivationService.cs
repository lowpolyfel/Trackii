using MySql.Data.MySqlClient;
using Trackii.Models.Admin.WorkOrderReactivation;

namespace Trackii.Services.Admin;

public class WorkOrderReactivationService
{
    private readonly string _conn;

    public WorkOrderReactivationService(IConfiguration cfg)
    {
        _conn = cfg.GetConnectionString("TrackiiDb")
            ?? throw new Exception("Connection string TrackiiDb no configurada");
    }

    public WorkOrderReactivationVm GetInactiveOrders(string? search)
    {
        var vm = new WorkOrderReactivationVm
        {
            Search = search
        };

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(@"
            SELECT wo.id,
                   wo.wo_number,
                   p.part_number,
                   wo.status,
                   rs.step_number,
                   l.name AS location_name
            FROM work_order wo
            LEFT JOIN product p ON p.id = wo.product_id
            LEFT JOIN wip_item wip ON wip.wo_order_id = wo.id
            LEFT JOIN route_step rs ON rs.id = wip.current_step_id
            LEFT JOIN location l ON l.id = rs.location_id
            WHERE wo.active = 0
              AND (@search = ''
                   OR wo.wo_number LIKE CONCAT('%', @search, '%')
                   OR p.part_number LIKE CONCAT('%', @search, '%'))
            ORDER BY wo.id DESC", cn);

        cmd.Parameters.AddWithValue("@search", search?.Trim() ?? string.Empty);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var stepLabel = rd.IsDBNull("step_number")
                ? "Sin paso registrado"
                : $"Paso {rd.GetInt32("step_number")} - {(rd.IsDBNull("location_name") ? "Sin localidad" : rd.GetString("location_name"))}";

            vm.Items.Add(new WorkOrderReactivationRowVm
            {
                WorkOrderId = rd.GetUInt32("id"),
                WoNumber = rd.GetString("wo_number"),
                PartNumber = rd.IsDBNull("part_number") ? "-" : rd.GetString("part_number"),
                Status = rd.GetString("status"),
                CancelledAtStep = stepLabel
            });
        }

        return vm;
    }

    public bool Reactivate(uint workOrderId)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();
        using var tx = cn.BeginTransaction();

        try
        {
            int affected;
            using (var woCmd = new MySqlCommand(@"
                UPDATE work_order
                SET active = 1,
                    status = 'IN_PROGRESS'
                WHERE id = @id
                  AND active = 0", cn, tx))
            {
                woCmd.Parameters.AddWithValue("@id", workOrderId);
                affected = woCmd.ExecuteNonQuery();
            }

            if (affected == 0)
            {
                tx.Rollback();
                return false;
            }

            using (var wipCmd = new MySqlCommand(@"
                UPDATE wip_item
                SET status = 'ACTIVE'
                WHERE wo_order_id = @id
                  AND status IN ('SCRAPPED', 'HOLD')", cn, tx))
            {
                wipCmd.Parameters.AddWithValue("@id", workOrderId);
                wipCmd.ExecuteNonQuery();
            }

            tx.Commit();
            return true;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
