using MySql.Data.MySqlClient;
using Trackii.Models.Admin.Wip;

namespace Trackii.Services.Admin;

public class AdminWipService
{
    private const string AdminDeviceUid = "ADMIN-WEB-PORTAL";
    private readonly string _conn;

    public AdminWipService(IConfiguration cfg)
    {
        _conn = cfg.GetConnectionString("TrackiiDb")
            ?? throw new Exception("Connection string TrackiiDb no configurada");
    }

    public AdminWipManagerVm GetInitialVm()
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();
        return new AdminWipManagerVm
        {
            ErrorCodes = GetErrorCodes(cn)
        };
    }

    public AdminWipManagerVm LoadOrder(string woNumber, string partNumber)
    {
        if (string.IsNullOrWhiteSpace(woNumber)) throw new Exception("WO Number es requerido.");
        if (string.IsNullOrWhiteSpace(partNumber)) throw new Exception("Part Number es requerido.");

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        var product = GetProductByPartNumber(cn, partNumber.Trim())
            ?? throw new Exception("El producto no existe o está inactivo. Regístralo primero.");

        var activeRouteId = GetActiveRouteId(cn, product.SubfamilyId);
        if (activeRouteId == 0)
            throw new Exception("La subfamilia del producto no tiene ruta activa.");

        var vm = new AdminWipManagerVm
        {
            WoNumber = woNumber.Trim(),
            PartNumber = partNumber.Trim(),
            ErrorCodes = GetErrorCodes(cn)
        };

        var workOrder = GetWorkOrder(cn, vm.WoNumber);
        if (workOrder is null)
        {
            vm.IsNewOrder = true;
            vm.RouteSteps = GetRouteSteps(cn, activeRouteId, null);
            return vm;
        }

        if (workOrder.Value.ProductId != product.ProductId)
            throw new Exception("La WO existe pero pertenece a otro producto.");

        vm.IsNewOrder = false;
        vm.WorkOrderId = workOrder.Value.WorkOrderId;

        var wipItem = GetWipItem(cn, vm.WorkOrderId);
        if (wipItem.HasValue)
        {
            vm.WipItemId = wipItem.Value.WipItemId;
        }

        vm.RouteSteps = GetRouteSteps(cn, activeRouteId, vm.WipItemId == 0 ? null : vm.WipItemId);
        return vm;
    }

    public void SaveProgress(AdminWipManagerVm model, string username)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();
        using var tx = cn.BeginTransaction();

        try
        {
            var userId = GetUserId(cn, tx, username);
            var deviceId = GetOrCreateAdminDevice(cn, tx);

            var product = GetProductByPartNumber(cn, model.PartNumber.Trim(), tx)
                ?? throw new Exception("Producto inválido.");
            var activeRouteId = GetActiveRouteId(cn, product.SubfamilyId, tx);
            if (activeRouteId == 0) throw new Exception("No existe ruta activa para este producto.");

            uint woId = model.WorkOrderId;
            uint wipId = model.WipItemId;

            if (model.IsNewOrder)
            {
                woId = InsertWorkOrder(cn, tx, model.WoNumber.Trim(), product.ProductId);
                wipId = InsertWipItem(cn, tx, woId, activeRouteId);
            }
            else
            {
                if (woId == 0)
                {
                    var wo = GetWorkOrder(cn, model.WoNumber.Trim(), tx)
                        ?? throw new Exception("No se encontró la orden.");
                    woId = wo.WorkOrderId;
                }

                if (wipId == 0)
                {
                    wipId = InsertWipItem(cn, tx, woId, activeRouteId);
                }
            }

            var existingCompletedStepIds = GetCompletedStepIds(cn, tx, wipId);
            var sortedSteps = model.RouteSteps.OrderBy(step => step.StepNumber).ToList();
            uint lastCompletedStepId = 0;

            var anyStepCaptured = false;
            for (var index = 0; index < sortedSteps.Count; index++)
            {
                var step = sortedSteps[index];
                var hasData = step.QtyIn > 0 || step.QtyScrap > 0;
                if (!hasData)
                {
                    continue;
                }

                anyStepCaptured = true;

                if (index > 0)
                {
                    var previousStep = sortedSteps[index - 1];
                    var previousCompleted = existingCompletedStepIds.Contains(previousStep.RouteStepId)
                                            || previousStep.QtyIn > 0;
                    if (!previousCompleted)
                        throw new Exception($"No puedes registrar el paso {step.StepNumber} sin completar el paso {previousStep.StepNumber}.");
                }

                UpsertStepExecution(cn, tx, wipId, step, userId, deviceId);

                if (step.QtyScrap > 0)
                {
                    if (!step.ErrorCodeId.HasValue)
                        throw new Exception($"El paso {step.StepNumber} requiere ErrorCode al reportar scrap.");

                    InsertScrapLog(cn, tx, wipId, step.RouteStepId, step.ErrorCodeId.Value, userId, step.QtyScrap, step.ScrapComments);
                }

                lastCompletedStepId = step.RouteStepId;
            }

            if (anyStepCaptured)
            {
                SetWipCurrentStep(cn, tx, wipId, lastCompletedStepId);
                var lastRouteStepId = GetLastRouteStepId(cn, tx, activeRouteId);
                if (lastCompletedStepId == lastRouteStepId)
                {
                    UpdateWorkOrderStatus(cn, tx, woId, "FINISHED");
                    UpdateWipStatus(cn, tx, wipId, "FINISHED");
                }
                else
                {
                    UpdateWorkOrderStatus(cn, tx, woId, "IN_PROGRESS");
                    UpdateWipStatus(cn, tx, wipId, "ACTIVE");
                }
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static (uint ProductId, uint SubfamilyId)? GetProductByPartNumber(MySqlConnection cn, string partNumber, MySqlTransaction? tx = null)
    {
        using var cmd = new MySqlCommand(@"
            SELECT id, id_subfamily
            FROM product
            WHERE part_number=@part AND active=1
            LIMIT 1", cn, tx);

        cmd.Parameters.AddWithValue("@part", partNumber);
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;

        return (rd.GetUInt32("id"), rd.GetUInt32("id_subfamily"));
    }

    private static uint GetActiveRouteId(MySqlConnection cn, uint subfamilyId, MySqlTransaction? tx = null)
    {
        using var cmd = new MySqlCommand("SELECT active_route_id FROM subfamily WHERE id=@id LIMIT 1", cn, tx);
        cmd.Parameters.AddWithValue("@id", subfamilyId);
        var scalar = cmd.ExecuteScalar();
        return scalar is null || scalar == DBNull.Value ? 0 : Convert.ToUInt32(scalar);
    }

    private static (uint WorkOrderId, uint ProductId)? GetWorkOrder(MySqlConnection cn, string woNumber, MySqlTransaction? tx = null)
    {
        using var cmd = new MySqlCommand(@"
            SELECT id, product_id
            FROM work_order
            WHERE wo_number=@wo
            LIMIT 1", cn, tx);

        cmd.Parameters.AddWithValue("@wo", woNumber);
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;

        return (rd.GetUInt32("id"), rd.GetUInt32("product_id"));
    }

    private static (uint WipItemId, uint RouteId)? GetWipItem(MySqlConnection cn, uint workOrderId, MySqlTransaction? tx = null)
    {
        using var cmd = new MySqlCommand(@"
            SELECT id, route_id
            FROM wip_item
            WHERE wo_order_id=@wo
            LIMIT 1", cn, tx);

        cmd.Parameters.AddWithValue("@wo", workOrderId);
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;

        return (rd.GetUInt32("id"), rd.GetUInt32("route_id"));
    }

    private static List<WipStepVm> GetRouteSteps(MySqlConnection cn, uint routeId, uint? wipItemId)
    {
        var steps = new List<WipStepVm>();
        var executions = new Dictionary<uint, (int QtyIn, int QtyScrap)>();

        if (wipItemId.HasValue)
        {
            using var exCmd = new MySqlCommand(@"
                SELECT route_step_id, qty_in, qty_scrap
                FROM wip_step_execution
                WHERE wip_item_id=@wip", cn);
            exCmd.Parameters.AddWithValue("@wip", wipItemId.Value);

            using var exRd = exCmd.ExecuteReader();
            while (exRd.Read())
            {
                executions[exRd.GetUInt32("route_step_id")] = (exRd.GetInt32("qty_in"), exRd.GetInt32("qty_scrap"));
            }
        }

        using var cmd = new MySqlCommand(@"
            SELECT rs.id, rs.step_number, rs.location_id, l.name AS location_name
            FROM route_step rs
            JOIN location l ON l.id = rs.location_id
            WHERE rs.route_id=@route
            ORDER BY rs.step_number", cn);

        cmd.Parameters.AddWithValue("@route", routeId);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var stepId = rd.GetUInt32("id");
            var hasExecution = executions.TryGetValue(stepId, out var execution);
            steps.Add(new WipStepVm
            {
                RouteStepId = stepId,
                StepNumber = rd.GetInt32("step_number"),
                LocationId = rd.GetUInt32("location_id"),
                LocationName = rd.GetString("location_name"),
                QtyIn = hasExecution ? execution.QtyIn : 0,
                QtyScrap = hasExecution ? execution.QtyScrap : 0,
                IsCompleted = hasExecution
            });
        }

        return steps;
    }

    private static List<(uint Id, string Code, string Description)> GetErrorCodes(MySqlConnection cn)
    {
        var list = new List<(uint, string, string)>();

        using var cmd = new MySqlCommand(@"
            SELECT id, code, description
            FROM error_code
            ORDER BY code", cn);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            list.Add((rd.GetUInt32("id"), rd.GetString("code"), rd.GetString("description")));
        }

        return list;
    }

    private static uint GetUserId(MySqlConnection cn, MySqlTransaction tx, string username)
    {
        using var cmd = new MySqlCommand("SELECT id FROM `user` WHERE username=@u LIMIT 1", cn, tx);
        cmd.Parameters.AddWithValue("@u", username);
        var scalar = cmd.ExecuteScalar();
        if (scalar is null) throw new Exception("No se pudo resolver el usuario autenticado.");
        return Convert.ToUInt32(scalar);
    }

    private static uint GetOrCreateAdminDevice(MySqlConnection cn, MySqlTransaction tx)
    {
        using (var check = new MySqlCommand("SELECT id FROM devices WHERE device_uid=@uid LIMIT 1", cn, tx))
        {
            check.Parameters.AddWithValue("@uid", AdminDeviceUid);
            var existing = check.ExecuteScalar();
            if (existing != null) return Convert.ToUInt32(existing);
        }

        uint fallbackLocationId;
        using (var loc = new MySqlCommand("SELECT id FROM location WHERE active=1 ORDER BY id LIMIT 1", cn, tx))
        {
            var scalar = loc.ExecuteScalar();
            if (scalar is null) throw new Exception("No hay location activa para registrar ADMIN-WEB-PORTAL.");
            fallbackLocationId = Convert.ToUInt32(scalar);
        }

        using var ins = new MySqlCommand(@"
            INSERT INTO devices (device_uid, location_id, name, active)
            VALUES (@uid, @loc, 'Admin Web Portal', 1)", cn, tx);
        ins.Parameters.AddWithValue("@uid", AdminDeviceUid);
        ins.Parameters.AddWithValue("@loc", fallbackLocationId);
        ins.ExecuteNonQuery();
        return Convert.ToUInt32(ins.LastInsertedId);
    }

    private static uint InsertWorkOrder(MySqlConnection cn, MySqlTransaction tx, string woNumber, uint productId)
    {
        using var insert = new MySqlCommand(@"
            INSERT INTO work_order (wo_number, product_id, status)
            VALUES (@wo, @product, 'OPEN')", cn, tx);

        insert.Parameters.AddWithValue("@wo", woNumber);
        insert.Parameters.AddWithValue("@product", productId);
        insert.ExecuteNonQuery();
        return Convert.ToUInt32(insert.LastInsertedId);
    }

    private static uint InsertWipItem(MySqlConnection cn, MySqlTransaction tx, uint workOrderId, uint routeId)
    {
        var firstStepId = GetFirstRouteStepId(cn, tx, routeId);

        using var insert = new MySqlCommand(@"
            INSERT INTO wip_item (wo_order_id, current_step_id, status, created_at, route_id)
            VALUES (@wo, @step, 'ACTIVE', NOW(), @route)", cn, tx);

        insert.Parameters.AddWithValue("@wo", workOrderId);
        insert.Parameters.AddWithValue("@step", firstStepId);
        insert.Parameters.AddWithValue("@route", routeId);
        insert.ExecuteNonQuery();
        return Convert.ToUInt32(insert.LastInsertedId);
    }

    private static uint GetFirstRouteStepId(MySqlConnection cn, MySqlTransaction tx, uint routeId)
    {
        using var cmd = new MySqlCommand(@"
            SELECT id
            FROM route_step
            WHERE route_id=@route
            ORDER BY step_number
            LIMIT 1", cn, tx);

        cmd.Parameters.AddWithValue("@route", routeId);
        var scalar = cmd.ExecuteScalar();
        if (scalar is null) throw new Exception("La ruta activa no tiene pasos.");
        return Convert.ToUInt32(scalar);
    }

    private static HashSet<uint> GetCompletedStepIds(MySqlConnection cn, MySqlTransaction tx, uint wipItemId)
    {
        var set = new HashSet<uint>();

        using var cmd = new MySqlCommand(@"
            SELECT route_step_id
            FROM wip_step_execution
            WHERE wip_item_id=@wip", cn, tx);

        cmd.Parameters.AddWithValue("@wip", wipItemId);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            set.Add(rd.GetUInt32("route_step_id"));
        }

        return set;
    }

    private static void UpsertStepExecution(MySqlConnection cn, MySqlTransaction tx, uint wipId, WipStepVm step, uint userId, uint deviceId)
    {
        using var cmd = new MySqlCommand(@"
            INSERT INTO wip_step_execution
                (wip_item_id, route_step_id, user_id, device_id, location_id, create_at, qty_in, qty_scrap)
            VALUES
                (@wip, @step, @user, @device, @loc, NOW(), @qtyIn, @qtyScrap)
            ON DUPLICATE KEY UPDATE
                user_id=@user,
                device_id=@device,
                location_id=@loc,
                qty_in=@qtyIn,
                qty_scrap=@qtyScrap,
                create_at=NOW()", cn, tx);

        cmd.Parameters.AddWithValue("@wip", wipId);
        cmd.Parameters.AddWithValue("@step", step.RouteStepId);
        cmd.Parameters.AddWithValue("@user", userId);
        cmd.Parameters.AddWithValue("@device", deviceId);
        cmd.Parameters.AddWithValue("@loc", step.LocationId);
        cmd.Parameters.AddWithValue("@qtyIn", step.QtyIn);
        cmd.Parameters.AddWithValue("@qtyScrap", step.QtyScrap);
        cmd.ExecuteNonQuery();
    }

    private static void InsertScrapLog(MySqlConnection cn, MySqlTransaction tx, uint wipId, uint routeStepId, uint errorCodeId, uint userId, int qty, string? comments)
    {
        using var cmd = new MySqlCommand(@"
            INSERT INTO scrap_log
                (wip_item_id, route_step_id, error_code_id, user_id, qty, comments, created_at)
            VALUES
                (@wip, @step, @error, @user, @qty, @comments, NOW())", cn, tx);

        cmd.Parameters.AddWithValue("@wip", wipId);
        cmd.Parameters.AddWithValue("@step", routeStepId);
        cmd.Parameters.AddWithValue("@error", errorCodeId);
        cmd.Parameters.AddWithValue("@user", userId);
        cmd.Parameters.AddWithValue("@qty", qty);
        cmd.Parameters.AddWithValue("@comments", string.IsNullOrWhiteSpace(comments) ? DBNull.Value : comments);
        cmd.ExecuteNonQuery();
    }

    private static void SetWipCurrentStep(MySqlConnection cn, MySqlTransaction tx, uint wipId, uint routeStepId)
    {
        using var cmd = new MySqlCommand("UPDATE wip_item SET current_step_id=@step WHERE id=@id", cn, tx);
        cmd.Parameters.AddWithValue("@step", routeStepId);
        cmd.Parameters.AddWithValue("@id", wipId);
        cmd.ExecuteNonQuery();
    }

    private static uint GetLastRouteStepId(MySqlConnection cn, MySqlTransaction tx, uint routeId)
    {
        using var cmd = new MySqlCommand(@"
            SELECT id
            FROM route_step
            WHERE route_id=@route
            ORDER BY step_number DESC
            LIMIT 1", cn, tx);

        cmd.Parameters.AddWithValue("@route", routeId);
        var scalar = cmd.ExecuteScalar();
        if (scalar is null) throw new Exception("La ruta activa no tiene pasos.");
        return Convert.ToUInt32(scalar);
    }

    private static void UpdateWorkOrderStatus(MySqlConnection cn, MySqlTransaction tx, uint woId, string status)
    {
        using var cmd = new MySqlCommand("UPDATE work_order SET status=@status WHERE id=@id", cn, tx);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@id", woId);
        cmd.ExecuteNonQuery();
    }

    private static void UpdateWipStatus(MySqlConnection cn, MySqlTransaction tx, uint wipId, string status)
    {
        using var cmd = new MySqlCommand("UPDATE wip_item SET status=@status WHERE id=@id", cn, tx);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@id", wipId);
        cmd.ExecuteNonQuery();
    }
}
