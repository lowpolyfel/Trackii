using MySql.Data.MySqlClient;
using Trackii.Models.Engineering.OrderTools;

namespace Trackii.Services.Engineering;

public class OrderToolsService
{
    private readonly string _conn;

    public OrderToolsService(IConfiguration cfg)
    {
        _conn = cfg.GetConnectionString("TrackiiDb") ?? throw new Exception("Connection string TrackiiDb no configurada");
    }

    public OrderActivationVm GetOrdersForActivation(string? search)
    {
        var vm = new OrderActivationVm { Search = search };
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(@"
            SELECT wo.id, wo.wo_number, COALESCE(p.part_number,'-') AS part_number, wo.status, wo.active
            FROM work_order wo
            LEFT JOIN product p ON p.id = wo.product_id
            WHERE (@search = '' OR wo.wo_number LIKE CONCAT('%', @search, '%') OR p.part_number LIKE CONCAT('%', @search, '%'))
              AND (wo.active = 0 OR wo.status = 'CANCELLED')
            ORDER BY wo.id DESC", cn);
        cmd.Parameters.AddWithValue("@search", search?.Trim() ?? string.Empty);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            vm.Items.Add(new OrderActivationRowVm
            {
                WorkOrderId = rd.GetUInt32("id"),
                WoNumber = rd.GetString("wo_number"),
                PartNumber = rd.GetString("part_number"),
                Status = rd.GetString("status"),
                Active = rd.GetBoolean("active")
            });
        }

        return vm;
    }

    public bool ReactivateOrder(uint workOrderId)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();
        using var tx = cn.BeginTransaction();

        using var cmd = new MySqlCommand(@"
            UPDATE work_order
            SET active = 1,
                status = 'IN_PROGRESS'
            WHERE id = @id
              AND (active = 0 OR status = 'CANCELLED')", cn, tx);
        cmd.Parameters.AddWithValue("@id", workOrderId);
        var changed = cmd.ExecuteNonQuery() > 0;

        if (!changed)
        {
            tx.Rollback();
            return false;
        }

        using var wip = new MySqlCommand(@"
            UPDATE wip_item
            SET status = 'ACTIVE'
            WHERE wo_order_id = @id
              AND status IN ('SCRAPPED', 'HOLD')", cn, tx);
        wip.Parameters.AddWithValue("@id", workOrderId);
        wip.ExecuteNonQuery();

        tx.Commit();
        return true;
    }

    public OrderPiecesEditVm GetOrderPiecesEditor(string? search, string? selectedWo)
    {
        var vm = new OrderPiecesEditVm { Search = search, SelectedWo = selectedWo };
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using (var listCmd = new MySqlCommand(@"
            SELECT wo.id, wo.wo_number, COALESCE(p.part_number, '-') AS part_number
            FROM work_order wo
            LEFT JOIN product p ON p.id = wo.product_id
            WHERE (@search = '' OR wo.wo_number LIKE CONCAT('%', @search, '%') OR p.part_number LIKE CONCAT('%', @search, '%'))
            ORDER BY wo.id DESC
            LIMIT 30", cn))
        {
            listCmd.Parameters.AddWithValue("@search", search?.Trim() ?? string.Empty);
            using var rd = listCmd.ExecuteReader();
            while (rd.Read())
            {
                vm.Matches.Add(new OrderLookupRowVm
                {
                    WorkOrderId = rd.GetUInt32("id"),
                    WoNumber = rd.GetString("wo_number"),
                    PartNumber = rd.GetString("part_number")
                });
            }
        }

        if (string.IsNullOrWhiteSpace(selectedWo)) return vm;

        using var detailCmd = new MySqlCommand(@"
            SELECT wo.id, wo.wo_number, COALESCE(p.part_number,'-') AS part_number,
                   rs.id AS route_step_id, rs.step_number, COALESCE(l.name,'Sin localidad') AS location_name,
                   wse.qty_in, wse.qty_scrap
            FROM work_order wo
            LEFT JOIN product p ON p.id = wo.product_id
            JOIN wip_item wip ON wip.wo_order_id = wo.id
            JOIN wip_step_execution wse ON wse.wip_item_id = wip.id
            JOIN route_step rs ON rs.id = wse.route_step_id
            LEFT JOIN location l ON l.id = wse.location_id
            WHERE wo.wo_number = @wo
            ORDER BY rs.step_number", cn);
        detailCmd.Parameters.AddWithValue("@wo", selectedWo.Trim());

        using var dr = detailCmd.ExecuteReader();
        OrderPiecesDetailVm? detail = null;
        while (dr.Read())
        {
            detail ??= new OrderPiecesDetailVm
            {
                WorkOrderId = dr.GetUInt32("id"),
                WoNumber = dr.GetString("wo_number"),
                PartNumber = dr.GetString("part_number")
            };
            detail.Steps.Add(new OrderStepQtyVm
            {
                RouteStepId = dr.GetUInt32("route_step_id"),
                StepNumber = dr.GetInt32("step_number"),
                Location = dr.GetString("location_name"),
                QtyIn = dr.GetInt32("qty_in"),
                QtyScrap = dr.GetInt32("qty_scrap")
            });
        }

        vm.Detail = detail;
        return vm;
    }

    public (bool ok, string message) UpdateStepQuantity(uint workOrderId, uint routeStepId, int newQty, bool cascade)
    {
        if (newQty < 0) return (false, "La cantidad no puede ser negativa.");

        using var cn = new MySqlConnection(_conn);
        cn.Open();
        using var tx = cn.BeginTransaction();

        var steps = new List<(uint stepId, int stepNo, int qtyIn)>();
        using (var cmd = new MySqlCommand(@"
            SELECT rs.id AS step_id, rs.step_number, wse.qty_in
            FROM wip_item wip
            JOIN wip_step_execution wse ON wse.wip_item_id = wip.id
            JOIN route_step rs ON rs.id = wse.route_step_id
            WHERE wip.wo_order_id = @wo
            ORDER BY rs.step_number", cn, tx))
        {
            cmd.Parameters.AddWithValue("@wo", workOrderId);
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) steps.Add((rd.GetUInt32("step_id"), rd.GetInt32("step_number"), rd.GetInt32("qty_in")));
        }

        var idx = steps.FindIndex(s => s.stepId == routeStepId);
        if (idx < 0)
        {
            tx.Rollback();
            return (false, "Paso no encontrado para la orden seleccionada.");
        }

        if (!cascade)
        {
            var downstreamHigher = steps.Skip(idx + 1).Any(s => s.qtyIn > newQty);
            if (downstreamHigher)
            {
                tx.Rollback();
                return (false, "Hay pasos posteriores con piezas mayores al nuevo valor. Activa 'ajustar siguientes pasos' para conservar integridad.");
            }
        }

        using (var up = new MySqlCommand("UPDATE wip_step_execution wse JOIN wip_item wip ON wip.id=wse.wip_item_id SET wse.qty_in=@qty WHERE wip.wo_order_id=@wo AND wse.route_step_id=@step", cn, tx))
        {
            up.Parameters.AddWithValue("@qty", newQty);
            up.Parameters.AddWithValue("@wo", workOrderId);
            up.Parameters.AddWithValue("@step", routeStepId);
            up.ExecuteNonQuery();
        }

        if (cascade)
        {
            using var cascadeCmd = new MySqlCommand(@"
                UPDATE wip_step_execution wse
                JOIN wip_item wip ON wip.id = wse.wip_item_id
                JOIN route_step rs ON rs.id = wse.route_step_id
                JOIN route_step target ON target.id = @step
                SET wse.qty_in = LEAST(wse.qty_in, @qty)
                WHERE wip.wo_order_id = @wo
                  AND rs.step_number >= target.step_number", cn, tx);
            cascadeCmd.Parameters.AddWithValue("@step", routeStepId);
            cascadeCmd.Parameters.AddWithValue("@qty", newQty);
            cascadeCmd.Parameters.AddWithValue("@wo", workOrderId);
            cascadeCmd.ExecuteNonQuery();
        }

        using var recalc = new MySqlCommand(@"
            UPDATE wip_step_execution wse
            JOIN wip_item wip ON wip.id = wse.wip_item_id
            JOIN (
                SELECT x.id,
                       GREATEST(COALESCE(LAG(x.qty_in) OVER (PARTITION BY x.wip_item_id ORDER BY x.create_at, x.id), x.qty_in) - x.qty_in, 0) AS calc_scrap
                FROM wip_step_execution x
                JOIN wip_item wi ON wi.id = x.wip_item_id
                WHERE wi.wo_order_id = @wo
            ) r ON r.id = wse.id
            SET wse.qty_scrap = r.calc_scrap
            WHERE wip.wo_order_id = @wo", cn, tx);
        recalc.Parameters.AddWithValue("@wo", workOrderId);
        recalc.ExecuteNonQuery();

        tx.Commit();
        return (true, "Piezas y scrap recalculado correctamente.");
    }
}
