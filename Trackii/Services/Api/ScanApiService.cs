using MySql.Data.MySqlClient;
using System.Data;
using Trackii.Models.Api;

namespace Trackii.Services.Api;

public class ScanApiService
{
    private readonly string _conn;

    public ScanApiService(IConfiguration cfg)
    {
        _conn = cfg.GetConnectionString("TrackiiDb")
            ?? throw new Exception("Connection string TrackiiDb no configurada");
    }

    // =========================================================
    // SCAN  (FASE 2: LOTE + NO. PARTE)
    // =========================================================
    public ScanResult Scan(
        uint userId,
        uint deviceId,
        string lot,
        string partNumber,
        uint qty)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();
        using var tx = cn.BeginTransaction();

        try
        {
            // 1) Device location
            var devLoc = GetDeviceLocation(cn, tx, deviceId);
            if (devLoc == null)
            {
                tx.Commit();
                return new ScanResult
                {
                    Ok = false,
                    Status = "NONE",
                    Reason = "DEVICE_INVALID"
                };
            }

            var (deviceLocationId, _) = devLoc.Value;

            // 2) Product by part number
            var productId = GetProductId(cn, tx, partNumber);
            if (productId == null)
            {
                tx.Commit();
                return new ScanResult
                {
                    Ok = false,
                    Status = "NONE",
                    Reason = "PRODUCT_NOT_FOUND"
                };
            }

            // 3) Work order por LOTE (wo_number = lot)
            var workOrderId = GetOrCreateWorkOrder(cn, tx, lot, productId.Value);

            // 4) Active route
            var activeRouteId = GetActiveRouteId(cn, tx, productId.Value);
            if (activeRouteId == 0)
            {
                tx.Commit();
                return new ScanResult
                {
                    Ok = false,
                    Status = "NONE",
                    Reason = "NO_ACTIVE_ROUTE"
                };
            }

            // 5) Get or create WIP (locked)
            var (wipItemId, wipStatus, routeId, currentStepId) =
                GetOrCreateWipLocked(cn, tx, workOrderId, activeRouteId);

            if (wipStatus == "HOLD")
            {
                tx.Commit();
                return new ScanResult
                {
                    Ok = false,
                    Status = "HOLD",
                    Reason = "WIP_ON_REWORK"
                };
            }


            if (wipStatus is "FINISHED" or "SCRAPPED")
            {
                InsertScanEvent(cn, tx, wipItemId, currentStepId, "ERROR");
                tx.Commit();
                return new ScanResult
                {
                    Ok = false,
                    Status = wipStatus,
                    Reason = "WIP_CLOSED"
                };
            }

            // 6) Current step meta
            var stepMeta = GetStepMetaLocked(cn, tx, routeId, currentStepId);
            if (stepMeta == null)
            {
                InsertScanEvent(cn, tx, wipItemId, currentStepId, "ERROR");
                tx.Commit();
                return new ScanResult
                {
                    Ok = false,
                    Status = "NONE",
                    Reason = "STEP_INVALID"
                };
            }

            var (currentStepNumber, expectedLocationId) = stepMeta.Value;

            // 7) Validate station
            if (expectedLocationId != deviceLocationId)
            {
                InsertScanEvent(cn, tx, wipItemId, currentStepId, "ERROR");
                tx.Commit();

                return new ScanResult
                {
                    Ok = false,
                    Status = "ACTIVE",
                    Reason = "STEP_MISMATCH",
                    CurrentStep = currentStepNumber,
                    ExpectedLocation = GetLocationName(cn, tx, expectedLocationId)
                };
            }

            // 8) Previous qty
            uint? previousQty = GetPreviousQty(cn, tx, wipItemId, routeId, currentStepNumber);

            // 9) Qty validation
            if (previousQty.HasValue && qty > previousQty.Value)
            {
                InsertScanEvent(cn, tx, wipItemId, currentStepId, "ERROR");
                tx.Commit();

                return new ScanResult
                {
                    Ok = false,
                    Status = "ACTIVE",
                    Reason = "QTY_GREATER_THAN_PREVIOUS",
                    CurrentStep = currentStepNumber,
                    PreviousQty = previousQty
                };
            }

            // 10) Scrap calculation
            uint scrap = previousQty.HasValue ? previousQty.Value - qty : 0;

            // 11) Prevent double exit
            if (HasExitForStepLocked(cn, tx, wipItemId, currentStepId))
            {
                InsertScanEvent(cn, tx, wipItemId, currentStepId, "ERROR");
                tx.Commit();

                return new ScanResult
                {
                    Ok = false,
                    Status = "ACTIVE",
                    Reason = "STEP_ALREADY_COMPLETED",
                    CurrentStep = currentStepNumber
                };
            }

            // 12) ENTRY
            InsertScanEvent(cn, tx, wipItemId, currentStepId, "ENTRY");

            // 13) Step execution
            using (var ins = new MySqlCommand(@"
                INSERT INTO wip_step_execution
                    (wip_item_id, route_step_id, user_id, device_id, location_id, create_at, qty_in, qty_scrap)
                VALUES
                    (@wip, @step, @user, @dev, @loc, NOW(), @qty, @scrap)
                ON DUPLICATE KEY UPDATE
                    qty_in=@qty, qty_scrap=@scrap, create_at=NOW(),
                    user_id=@user, device_id=@dev, location_id=@loc;", cn, tx))
            {
                ins.Parameters.AddWithValue("@wip", wipItemId);
                ins.Parameters.AddWithValue("@step", currentStepId);
                ins.Parameters.AddWithValue("@user", userId);
                ins.Parameters.AddWithValue("@dev", deviceId);
                ins.Parameters.AddWithValue("@loc", deviceLocationId);
                ins.Parameters.AddWithValue("@qty", qty);
                ins.Parameters.AddWithValue("@scrap", scrap);
                ins.ExecuteNonQuery();
            }

            // 14) EXIT
            InsertScanEvent(cn, tx, wipItemId, currentStepId, "EXIT");

            // 15) Scrap total
            if (qty == 0)
            {
                using var up = new MySqlCommand(
                    "UPDATE wip_item SET status='SCRAPPED' WHERE id=@id", cn, tx);
                up.Parameters.AddWithValue("@id", wipItemId);
                up.ExecuteNonQuery();

                tx.Commit();
                return new ScanResult
                {
                    Ok = true,
                    Status = "SCRAPPED",
                    QtyIn = qty,
                    PreviousQty = previousQty,
                    Scrap = previousQty ?? 0
                };
            }

            // 16) Next step
            var next = GetNextStepMeta(cn, tx, routeId, currentStepNumber + 1);
            if (next == null)
            {
                using var fin = new MySqlCommand(
                    "UPDATE wip_item SET status='FINISHED' WHERE id=@id", cn, tx);
                fin.Parameters.AddWithValue("@id", wipItemId);
                fin.ExecuteNonQuery();

                tx.Commit();
                return new ScanResult
                {
                    Ok = true,
                    Advanced = true,
                    Status = "FINISHED",
                    QtyIn = qty,
                    PreviousQty = previousQty,
                    Scrap = scrap
                };
            }

            var (nextStepId, nextLocationId) = next.Value;

            using (var adv = new MySqlCommand(
                "UPDATE wip_item SET current_step_id=@s WHERE id=@id", cn, tx))
            {
                adv.Parameters.AddWithValue("@s", nextStepId);
                adv.Parameters.AddWithValue("@id", wipItemId);
                adv.ExecuteNonQuery();
            }

            tx.Commit();
            return new ScanResult
            {
                Ok = true,
                Advanced = true,
                Status = "ACTIVE",
                CurrentStep = currentStepNumber,
                QtyIn = qty,
                PreviousQty = previousQty,
                Scrap = scrap,
                NextStep = currentStepNumber + 1,
                NextLocation = GetLocationNameNoTx(cn, nextLocationId)
            };
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
    public ScanResolveResponse Resolve(uint userId, uint deviceId, string lot, string partNumber)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();
        using var tx = cn.BeginTransaction();

        try
        {
            // Device location (requiere location)
            var devLoc = GetDeviceLocation(cn, tx, deviceId);
            if (devLoc == null)
            {
                tx.Commit();
                return new ScanResolveResponse { Ok = false, Reason = "DEVICE_INVALID_OR_NO_LOCATION" };
            }

            var (deviceLocationId, deviceLocationName) = devLoc.Value;

            // Product by part number
            var productId = GetProductId(cn, tx, partNumber);
            if (productId == null)
            {
                tx.Commit();
                return new ScanResolveResponse { Ok = false, Reason = "PRODUCT_NOT_FOUND" };
            }

            // Product hierarchy + names
            string areaName = "", familyName = "", subfamilyName = "", productName = "";
            uint routeId = 0;
            int routeVersion = 0;

            using (var cmd = new MySqlCommand(@"
            SELECT 
                p.id AS product_id,
                COALESCE(p.name,'') AS product_name,
                COALESCE(a.name,'') AS area_name,
                COALESCE(f.name,'') AS family_name,
                COALESCE(sf.name,'') AS subfamily_name,
                COALESCE(sf.active_route_id,0) AS route_id
            FROM product p
            JOIN subfamily sf ON sf.id = p.id_subfamily
            JOIN family f ON f.id = sf.family_id
            JOIN area a ON a.id = f.area_id
            WHERE p.id=@pid
            LIMIT 1", cn, tx))
            {
                cmd.Parameters.AddWithValue("@pid", productId.Value);
                using var rd = cmd.ExecuteReader();
                if (!rd.Read())
                {
                    tx.Commit();
                    return new ScanResolveResponse { Ok = false, Reason = "PRODUCT_HIERARCHY_INVALID" };
                }

                productName = rd.GetString("product_name");
                areaName = rd.GetString("area_name");
                familyName = rd.GetString("family_name");
                subfamilyName = rd.GetString("subfamily_name");
                routeId = Convert.ToUInt32(rd.GetUInt32("route_id"));
            }

            if (routeId == 0)
            {
                tx.Commit();
                return new ScanResolveResponse { Ok = false, Reason = "NO_ACTIVE_ROUTE" };
            }

            // route version
            using (var rv = new MySqlCommand("SELECT version FROM route WHERE id=@id LIMIT 1", cn, tx))
            {
                rv.Parameters.AddWithValue("@id", routeId);
                var obj = rv.ExecuteScalar();
                routeVersion = obj == null ? 0 : Convert.ToInt32(obj);
            }

            // Work order (wo_number = lot)
            var workOrderId = GetOrCreateWorkOrder(cn, tx, lot, productId.Value);

            // Get or create WIP (locked)
            var (wipItemId, wipStatus, rId, currentStepId) = GetOrCreateWipLocked(cn, tx, workOrderId, routeId);

            if (wipStatus == "HOLD")
            {
                tx.Commit();
                return new ScanResolveResponse { Ok = false, Reason = "WIP_ON_REWORK" };
            }

            if (wipStatus is "FINISHED" or "SCRAPPED")
            {
                tx.Commit();
                return new ScanResolveResponse { Ok = false, Reason = "WIP_CLOSED" };
            }

            // Current step meta
            var stepMeta = GetStepMetaLocked(cn, tx, rId, currentStepId);
            if (stepMeta == null)
            {
                tx.Commit();
                return new ScanResolveResponse { Ok = false, Reason = "STEP_INVALID" };
            }

            var (currentStepNumber, expectedLocationId) = stepMeta.Value;

            // Expected location name
            var expectedLocName = GetLocationName(cn, tx, expectedLocationId) ?? "";

            // MaxQty = previous qty (si existe), si no existe -> suggested = 0? (o qty inicial)
            uint? previousQty = GetPreviousQty(cn, tx, wipItemId, rId, currentStepNumber);
            uint maxQty = previousQty ?? 0;

            // Si es primer paso y no hay previousQty, sugerimos maxQty = 0 (la app puede pedir qty manual).
            // Si quieres “qty inicial”, aquí es donde se define con una regla simple.
            uint suggestedQty = previousQty ?? 0;

            tx.Commit();
            return new ScanResolveResponse
            {
                Ok = true,
                ProductId = productId.Value,
                ProductName = productName,
                Area = areaName,
                Family = familyName,
                Subfamily = subfamilyName,
                RouteId = routeId,
                RouteVersion = routeVersion,
                CurrentStep = currentStepNumber,
                ExpectedLocation = expectedLocName,
                SuggestedQty = suggestedQty,
                MaxQty = maxQty
            };
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // =========================================================
    // QUICK STATUS
    // =========================================================
    public WoQuickStatusResult? GetQuickStatus(string woNumber)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();
        using var tx = cn.BeginTransaction();

        try
        {
            var woInfo = GetWorkOrderInfo(cn, tx, woNumber);
            if (woInfo == null)
            {
                tx.Commit();
                return null;
            }

            var (workOrderId, activeRouteId) = woInfo.Value;

            uint? wipItemId = null;
            string? status = null;
            uint? routeId = null;
            uint? currentStepId = null;

            using (var cmd = new MySqlCommand(@"
                SELECT id, status, route_id, current_step_id
                FROM wip_item
                WHERE wo_order_id=@wo
                LIMIT 1", cn, tx))
            {
                cmd.Parameters.AddWithValue("@wo", workOrderId);
                using var rd = cmd.ExecuteReader();
                if (rd.Read())
                {
                    wipItemId = rd.GetUInt32("id");
                    status = rd.GetString("status");
                    routeId = rd.GetUInt32("route_id");
                    currentStepId = rd.GetUInt32("current_step_id");
                }
            }

            if (wipItemId == null)
            {
                string? locName = null;
                if (activeRouteId != 0)
                {
                    using var s1 = new MySqlCommand(@"
                        SELECT l.name
                        FROM route_step rs
                        JOIN location l ON l.id = rs.location_id
                        WHERE rs.route_id=@r AND rs.step_number=1
                        LIMIT 1", cn, tx);
                    s1.Parameters.AddWithValue("@r", activeRouteId);
                    locName = Convert.ToString(s1.ExecuteScalar());
                }

                tx.Commit();
                return new WoQuickStatusResult
                {
                    WoNumber = woNumber,
                    HasWip = false,
                    Status = "NONE",
                    CurrentStep = 1,
                    ExpectedLocation = locName,
                    QtyMax = null
                };
            }

            var meta = GetStepMetaLocked(cn, tx, routeId!.Value, currentStepId!.Value);
            if (meta == null)
            {
                tx.Commit();
                return new WoQuickStatusResult
                {
                    WoNumber = woNumber,
                    HasWip = true,
                    Status = status!,
                    CurrentStep = null,
                    ExpectedLocation = null,
                    QtyMax = null
                };
            }

            var (stepNumber, locId) = meta.Value;
            var prevQty = GetPreviousQty(cn, tx, wipItemId.Value, routeId.Value, stepNumber);

            tx.Commit();
            return new WoQuickStatusResult
            {
                WoNumber = woNumber,
                HasWip = true,
                Status = status!,
                CurrentStep = stepNumber,
                ExpectedLocation = GetLocationName(cn, tx, locId),
                QtyMax = prevQty
            };
        }
        catch
        {
            tx.Rollback();
            throw;
        }


    }

    // =========================================================
    // HELPERS
    // =========================================================
    private static (uint LocationId, string LocationName)? GetDeviceLocation(
        MySqlConnection cn, MySqlTransaction tx, uint deviceId)
    {
        using var cmd = new MySqlCommand(@"
            SELECT d.location_id, l.name
            FROM devices d
            JOIN location l ON l.id = d.location_id
            WHERE d.id=@id AND d.active=1
            LIMIT 1", cn, tx);

        cmd.Parameters.AddWithValue("@id", deviceId);
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;

        return (rd.GetUInt32("location_id"), rd.GetString("name"));
    }

    private static uint? GetProductId(
        MySqlConnection cn, MySqlTransaction tx, string partNumber)
    {
        using var cmd = new MySqlCommand(
            "SELECT id FROM product WHERE part_number=@p AND active=1 LIMIT 1", cn, tx);
        cmd.Parameters.AddWithValue("@p", partNumber);
        var obj = cmd.ExecuteScalar();
        return obj == null ? null : Convert.ToUInt32(obj);
    }

    private static uint GetOrCreateWorkOrder(
        MySqlConnection cn, MySqlTransaction tx,
        string lot, uint productId)
    {
        using (var q = new MySqlCommand(
            "SELECT id FROM work_order WHERE wo_number=@wo LIMIT 1 FOR UPDATE", cn, tx))
        {
            q.Parameters.AddWithValue("@wo", lot);
            var obj = q.ExecuteScalar();
            if (obj != null) return Convert.ToUInt32(obj);
        }

        using (var ins = new MySqlCommand(@"
            INSERT INTO work_order (wo_number, product_id, status)
            VALUES (@wo, @p, 'OPEN')", cn, tx))
        {
            ins.Parameters.AddWithValue("@wo", lot);
            ins.Parameters.AddWithValue("@p", productId);
            ins.ExecuteNonQuery();
            return Convert.ToUInt32(ins.LastInsertedId);
        }
    }

    private static uint GetActiveRouteId(
        MySqlConnection cn, MySqlTransaction tx, uint productId)
    {
        using var cmd = new MySqlCommand(@"
            SELECT sf.active_route_id
            FROM product p
            JOIN subfamily sf ON sf.id = p.id_subfamily
            WHERE p.id=@p
            LIMIT 1", cn, tx);

        cmd.Parameters.AddWithValue("@p", productId);
        var obj = cmd.ExecuteScalar();
        return obj == null ? 0u : Convert.ToUInt32(obj);
    }

    private static (uint WorkOrderId, uint ActiveRouteId)? GetWorkOrderInfo(
        MySqlConnection cn, MySqlTransaction tx, string woNumber)
    {
        using var cmd = new MySqlCommand(@"
            SELECT wo.id AS wo_id, sf.active_route_id
            FROM work_order wo
            JOIN product p ON p.id = wo.product_id
            JOIN subfamily sf ON sf.id = p.id_subfamily
            WHERE wo.wo_number=@wo
            LIMIT 1", cn, tx);

        cmd.Parameters.AddWithValue("@wo", woNumber);
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;

        return (
            rd.GetUInt32("wo_id"),
            rd.IsDBNull("active_route_id") ? 0u : rd.GetUInt32("active_route_id")
        );
    }

    private static (uint WipItemId, string Status, uint RouteId, uint CurrentStepId)
        GetOrCreateWipLocked(
            MySqlConnection cn, MySqlTransaction tx,
            uint workOrderId, uint activeRouteId)
    {
        using (var q = new MySqlCommand(@"
            SELECT id, status, route_id, current_step_id
            FROM wip_item
            WHERE wo_order_id=@wo
            LIMIT 1
            FOR UPDATE", cn, tx))
        {
            q.Parameters.AddWithValue("@wo", workOrderId);
            using var rd = q.ExecuteReader();
            if (rd.Read())
            {
                return (
                    rd.GetUInt32("id"),
                    rd.GetString("status"),
                    rd.GetUInt32("route_id"),
                    rd.GetUInt32("current_step_id")
                );
            }
        }

        uint step1Id;
        using (var s1 = new MySqlCommand(@"
            SELECT id
            FROM route_step
            WHERE route_id=@r AND step_number=1
            LIMIT 1", cn, tx))
        {
            s1.Parameters.AddWithValue("@r", activeRouteId);
            step1Id = Convert.ToUInt32(
                s1.ExecuteScalar() ?? throw new Exception("Ruta sin step 1"));
        }

        using (var ins = new MySqlCommand(@"
            INSERT INTO wip_item
                (wo_order_id, current_step_id, status, created_at, route_id)
            VALUES (@wo, @step, 'ACTIVE', NOW(), @route)", cn, tx))
        {
            ins.Parameters.AddWithValue("@wo", workOrderId);
            ins.Parameters.AddWithValue("@step", step1Id);
            ins.Parameters.AddWithValue("@route", activeRouteId);
            ins.ExecuteNonQuery();
            return (Convert.ToUInt32(ins.LastInsertedId), "ACTIVE", activeRouteId, step1Id);
        }
    }

    private static (uint StepNumber, uint LocationId)? GetStepMetaLocked(
        MySqlConnection cn, MySqlTransaction tx,
        uint routeId, uint stepId)
    {
        using var cmd = new MySqlCommand(@"
            SELECT step_number, location_id
            FROM route_step
            WHERE id=@id AND route_id=@r
            LIMIT 1
            FOR UPDATE", cn, tx);

        cmd.Parameters.AddWithValue("@id", stepId);
        cmd.Parameters.AddWithValue("@r", routeId);

        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;

        return (rd.GetUInt32("step_number"), rd.GetUInt32("location_id"));
    }

    private static (uint NextStepId, uint NextLocationId)? GetNextStepMeta(
        MySqlConnection cn, MySqlTransaction tx,
        uint routeId, uint stepNumber)
    {
        using var cmd = new MySqlCommand(@"
            SELECT id, location_id
            FROM route_step
            WHERE route_id=@r AND step_number=@n
            LIMIT 1", cn, tx);

        cmd.Parameters.AddWithValue("@r", routeId);
        cmd.Parameters.AddWithValue("@n", stepNumber);

        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;

        return (rd.GetUInt32("id"), rd.GetUInt32("location_id"));
    }

    private static uint? GetPreviousQty(
        MySqlConnection cn, MySqlTransaction tx,
        uint wipItemId, uint routeId, uint stepNumber)
    {
        if (stepNumber <= 1) return null;

        using var cmd = new MySqlCommand(@"
            SELECT wse.qty_in
            FROM wip_step_execution wse
            JOIN route_step rs ON rs.id = wse.route_step_id
            WHERE wse.wip_item_id=@wip
              AND rs.route_id=@r
              AND rs.step_number=@prev
            LIMIT 1", cn, tx);

        cmd.Parameters.AddWithValue("@wip", wipItemId);
        cmd.Parameters.AddWithValue("@r", routeId);
        cmd.Parameters.AddWithValue("@prev", stepNumber - 1);

        var obj = cmd.ExecuteScalar();
        return obj == null ? null : Convert.ToUInt32(obj);
    }

    private static bool HasExitForStepLocked(
        MySqlConnection cn, MySqlTransaction tx,
        uint wipItemId, uint routeStepId)
    {
        using var cmd = new MySqlCommand(@"
            SELECT id
            FROM scan_event
            WHERE wip_item_id=@wip
              AND route_step_id=@step
              AND scan_type='EXIT'
            LIMIT 1
            FOR UPDATE", cn, tx);

        cmd.Parameters.AddWithValue("@wip", wipItemId);
        cmd.Parameters.AddWithValue("@step", routeStepId);

        return cmd.ExecuteScalar() != null;
    }

    private static string GetLocationName(
        MySqlConnection cn, MySqlTransaction tx, uint locId)
    {
        using var cmd = new MySqlCommand(
            "SELECT name FROM location WHERE id=@id LIMIT 1", cn, tx);
        cmd.Parameters.AddWithValue("@id", locId);
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }

    private static string GetLocationNameNoTx(
        MySqlConnection cn, uint locId)
    {
        using var cmd = new MySqlCommand(
            "SELECT name FROM location WHERE id=@id LIMIT 1", cn);
        cmd.Parameters.AddWithValue("@id", locId);
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }

    private static void InsertScanEvent(
        MySqlConnection cn, MySqlTransaction tx,
        uint wipItemId, uint routeStepId, string scanType)
    {
        using var cmd = new MySqlCommand(@"
            INSERT INTO scan_event (wip_item_id, route_step_id, scan_type, ts)
            VALUES (@wip, @step, @type, NOW())", cn, tx);

        cmd.Parameters.AddWithValue("@wip", wipItemId);
        cmd.Parameters.AddWithValue("@step", routeStepId);
        cmd.Parameters.AddWithValue("@type", scanType);
        cmd.ExecuteNonQuery();
    }

    public WipCancelResponse CancelWip(
    uint userId,
    uint deviceId,
    string lot,
    string partNumber,
    string? reason)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();
        using var tx = cn.BeginTransaction();

        try
        {
            // Device válido
            var devLoc = GetDeviceLocation(cn, tx, deviceId);
            if (devLoc == null)
            {
                tx.Commit();
                return new WipCancelResponse
                {
                    Ok = false,
                    Reason = "DEVICE_INVALID"
                };
            }

            // Producto
            var productId = GetProductId(cn, tx, partNumber);
            if (productId == null)
            {
                tx.Commit();
                return new WipCancelResponse
                {
                    Ok = false,
                    Reason = "PRODUCT_NOT_FOUND"
                };
            }

            // Work order por lote
            uint workOrderId;
            using (var q = new MySqlCommand(
                "SELECT id FROM work_order WHERE wo_number=@wo LIMIT 1", cn, tx))
            {
                q.Parameters.AddWithValue("@wo", lot);
                var obj = q.ExecuteScalar();
                if (obj == null)
                {
                    tx.Commit();
                    return new WipCancelResponse
                    {
                        Ok = false,
                        Reason = "WO_NOT_FOUND"
                    };
                }
                workOrderId = Convert.ToUInt32(obj);
            }

            // WIP
            uint wipItemId;
            string status;
            uint currentStepId;

            using (var q = new MySqlCommand(@"
            SELECT id, status, current_step_id
            FROM wip_item
            WHERE wo_order_id=@wo
            LIMIT 1
            FOR UPDATE", cn, tx))
            {
                q.Parameters.AddWithValue("@wo", workOrderId);
                using var rd = q.ExecuteReader();
                if (!rd.Read())
                {
                    tx.Commit();
                    return new WipCancelResponse
                    {
                        Ok = false,
                        Reason = "WIP_NOT_FOUND"
                    };
                }

                wipItemId = rd.GetUInt32("id");
                status = rd.GetString("status");
                currentStepId = rd.GetUInt32("current_step_id");
            }

            if (status is "SCRAPPED" or "FINISHED")
            {
                tx.Commit();
                return new WipCancelResponse
                {
                    Ok = false,
                    Reason = "WIP_CLOSED"
                };
            }

            // Marcar SCRAPPED
            using (var up = new MySqlCommand(
                "UPDATE wip_item SET status='SCRAPPED' WHERE id=@id", cn, tx))
            {
                up.Parameters.AddWithValue("@id", wipItemId);
                up.ExecuteNonQuery();
            }

            // Evento MANUAL de cancelación
            using (var ev = new MySqlCommand(@"
            INSERT INTO scan_event (wip_item_id, route_step_id, scan_type, ts)
            VALUES (@wip, @step, 'MANUAL', NOW())", cn, tx))
            {
                ev.Parameters.AddWithValue("@wip", wipItemId);
                ev.Parameters.AddWithValue("@step", currentStepId);
                ev.ExecuteNonQuery();
            }

            tx.Commit();
            return new WipCancelResponse
            {
                Ok = true,
                Status = "SCRAPPED"
            };
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }


    public WipReworkResponse StartRework(
    uint userId,
    uint deviceId,
    string lot,
    string partNumber,
    uint locationId,
    uint qty,
    string? reason)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();
        using var tx = cn.BeginTransaction();

        try
        {
            var productId = GetProductId(cn, tx, partNumber);
            if (productId == null)
            {
                tx.Commit();
                return new WipReworkResponse { Ok = false, Reason = "PRODUCT_NOT_FOUND" };
            }

            uint workOrderId;
            using (var q = new MySqlCommand(
                "SELECT id FROM work_order WHERE wo_number=@wo LIMIT 1", cn, tx))
            {
                q.Parameters.AddWithValue("@wo", lot);
                var obj = q.ExecuteScalar();
                if (obj == null)
                {
                    tx.Commit();
                    return new WipReworkResponse { Ok = false, Reason = "WO_NOT_FOUND" };
                }
                workOrderId = Convert.ToUInt32(obj);
            }

            uint wipItemId;
            string status;

            using (var q = new MySqlCommand(@"
            SELECT id, status
            FROM wip_item
            WHERE wo_order_id=@wo
            LIMIT 1
            FOR UPDATE", cn, tx))
            {
                q.Parameters.AddWithValue("@wo", workOrderId);
                using var rd = q.ExecuteReader();
                if (!rd.Read())
                {
                    tx.Commit();
                    return new WipReworkResponse { Ok = false, Reason = "WIP_NOT_FOUND" };
                }

                wipItemId = rd.GetUInt32("id");
                status = rd.GetString("status");
            }

            if (status is "SCRAPPED" or "FINISHED")
            {
                tx.Commit();
                return new WipReworkResponse { Ok = false, Reason = "WIP_CLOSED" };
            }

            using (var ins = new MySqlCommand(@"
            INSERT INTO wip_rework_log
                (wip_item_id, location_id, user_id, device_id, qty, reason, created_at)
            VALUES (@wip, @loc, @user, @dev, @qty, @reason, NOW())", cn, tx))
            {
                ins.Parameters.AddWithValue("@wip", wipItemId);
                ins.Parameters.AddWithValue("@loc", locationId);
                ins.Parameters.AddWithValue("@user", userId);
                ins.Parameters.AddWithValue("@dev", deviceId);
                ins.Parameters.AddWithValue("@qty", qty);
                ins.Parameters.AddWithValue("@reason", reason);
                ins.ExecuteNonQuery();
            }

            using (var up = new MySqlCommand(
                "UPDATE wip_item SET status='HOLD' WHERE id=@id", cn, tx))
            {
                up.Parameters.AddWithValue("@id", wipItemId);
                up.ExecuteNonQuery();
            }

            tx.Commit();
            return new WipReworkResponse
            {
                Ok = true,
                Status = "HOLD"
            };
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public WipReworkResponse ReleaseRework(string lot, string partNumber)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();
        using var tx = cn.BeginTransaction();

        try
        {
            uint workOrderId;
            using (var q = new MySqlCommand(
                "SELECT id FROM work_order WHERE wo_number=@wo LIMIT 1", cn, tx))
            {
                q.Parameters.AddWithValue("@wo", lot);
                var obj = q.ExecuteScalar();
                if (obj == null)
                {
                    tx.Commit();
                    return new WipReworkResponse { Ok = false, Reason = "WO_NOT_FOUND" };
                }
                workOrderId = Convert.ToUInt32(obj);
            }

            using (var up = new MySqlCommand(@"
            UPDATE wip_item
            SET status='ACTIVE'
            WHERE wo_order_id=@wo AND status='HOLD'", cn, tx))
            {
                up.Parameters.AddWithValue("@wo", workOrderId);
                up.ExecuteNonQuery();
            }

            tx.Commit();
            return new WipReworkResponse
            {
                Ok = true,
                Status = "ACTIVE"
            };
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }


}

// =========================================================
// DTOs
// =========================================================
public class ScanResult
{
    public bool Ok { get; set; }
    public bool Advanced { get; set; }
    public string Status { get; set; } = "";
    public string Reason { get; set; } = "";
    public uint? CurrentStep { get; set; }
    public string? ExpectedLocation { get; set; }
    public uint? QtyIn { get; set; }
    public uint? PreviousQty { get; set; }
    public uint? Scrap { get; set; }
    public uint? NextStep { get; set; }
    public string? NextLocation { get; set; }
}

public class WoQuickStatusResult
{
    public string WoNumber { get; set; } = "";
    public bool HasWip { get; set; }
    public string Status { get; set; } = "";
    public uint? CurrentStep { get; set; }
    public string? ExpectedLocation { get; set; }
    public uint? QtyMax { get; set; }
}
