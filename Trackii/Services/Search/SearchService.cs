using MySql.Data.MySqlClient;
using Trackii.Models.Search;

namespace Trackii.Services.Search;

public class SearchService
{
    private readonly string _conn;

    public SearchService(IConfiguration cfg)
    {
        _conn = cfg.GetConnectionString("TrackiiDb")
            ?? throw new Exception("Connection string TrackiiDb no configurada");
    }

    public SearchIndexVm Search(string? query)
    {
        var vm = new SearchIndexVm
        {
            Query = query?.Trim() ?? string.Empty
        };

        if (string.IsNullOrWhiteSpace(vm.Query))
            return vm;

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        var normalized = $"%{vm.Query}%";

        using (var cmd = new MySqlCommand(@"
            SELECT
                'Producto' AS result_type,
                p.id AS product_id,
                NULL AS work_order_id,
                p.part_number AS primary_value,
                'Part number' AS secondary_value,
                a.name AS area,
                f.name AS family,
                s.name AS subfamily,
                CASE WHEN p.active = 1 THEN 'Activo' ELSE 'Inactivo' END AS status,
                NULL AS current_location
            FROM product p
            JOIN subfamily s ON s.id = p.id_subfamily
            JOIN family f ON f.id = s.id_family
            JOIN area a ON a.id = f.id_area
            WHERE p.part_number LIKE @query

            UNION ALL

            SELECT
                'Work Order' AS result_type,
                p.id AS product_id,
                wo.id AS work_order_id,
                wo.wo_number AS primary_value,
                p.part_number AS secondary_value,
                a.name AS area,
                f.name AS family,
                s.name AS subfamily,
                wo.status AS status,
                l.name AS current_location
            FROM work_order wo
            JOIN product p ON p.id = wo.product_id
            JOIN subfamily s ON s.id = p.id_subfamily
            JOIN family f ON f.id = s.id_family
            JOIN area a ON a.id = f.id_area
            LEFT JOIN wip_item wip ON wip.wo_order_id = wo.id
            LEFT JOIN route_step rs ON rs.id = wip.current_step_id
            LEFT JOIN location l ON l.id = rs.location_id
            WHERE wo.wo_number LIKE @query

            ORDER BY primary_value
            LIMIT 50;", cn))
        {
            cmd.Parameters.AddWithValue("@query", normalized);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var workOrderOrdinal = rd.GetOrdinal("work_order_id");
                var locationOrdinal = rd.GetOrdinal("current_location");

                vm.Results.Add(new SearchResultVm
                {
                    Type = rd.GetString("result_type"),
                    ProductId = rd.GetUInt32("product_id"),
                    WorkOrderId = rd.IsDBNull(workOrderOrdinal) ? null : rd.GetUInt32("work_order_id"),
                    PrimaryValue = rd.GetString("primary_value"),
                    SecondaryValue = rd.GetString("secondary_value"),
                    Area = rd.GetString("area"),
                    Family = rd.GetString("family"),
                    Subfamily = rd.GetString("subfamily"),
                    Status = rd.GetString("status"),
                    CurrentLocation = rd.IsDBNull(locationOrdinal) ? null : rd.GetString("current_location")
                });
            }
        }

        return vm;
    }

    public SearchDetailVm? GetDetail(uint productId, uint? workOrderId)
    {
        using var cn = new MySqlConnection(_conn);
        cn.Open();

        var vm = new SearchDetailVm();

        using (var cmd = new MySqlCommand(@"
            SELECT
                p.id AS product_id,
                p.part_number,
                p.active AS product_active,
                a.name AS area,
                f.name AS family,
                s.name AS subfamily,
                r.id AS route_id,
                COALESCE(r.name, 'Ruta sin asignar') AS route_name,
                COALESCE(r.version, '-') AS route_version,
                COALESCE(r.active, 0) AS route_active
            FROM product p
            JOIN subfamily s ON s.id = p.id_subfamily
            JOIN family f ON f.id = s.id_family
            JOIN area a ON a.id = f.id_area
            LEFT JOIN route r ON r.id = s.active_route_id
            WHERE p.id = @productId
            LIMIT 1;", cn))
        {
            cmd.Parameters.AddWithValue("@productId", productId);
            using var rd = cmd.ExecuteReader();
            if (!rd.Read())
                return null;

            vm.ProductId = rd.GetUInt32("product_id");
            vm.PartNumber = rd.GetString("part_number");
            vm.ProductActive = rd.GetBoolean("product_active");
            vm.Area = rd.GetString("area");
            vm.Family = rd.GetString("family");
            vm.Subfamily = rd.GetString("subfamily");
            vm.RouteName = rd.GetString("route_name");
            vm.RouteVersion = rd.GetString("route_version");
            vm.RouteActive = Convert.ToBoolean(rd["route_active"]);
        }

        uint? routeId = null;
        using (var routeCmd = new MySqlCommand(@"
            SELECT s.active_route_id
            FROM product p
            JOIN subfamily s ON s.id = p.id_subfamily
            WHERE p.id = @productId
            LIMIT 1;", cn))
        {
            routeCmd.Parameters.AddWithValue("@productId", productId);
            var routeResult = routeCmd.ExecuteScalar();
            if (routeResult != null && routeResult != DBNull.Value)
            {
                routeId = Convert.ToUInt32(routeResult);
            }
        }

        if (workOrderId.HasValue)
        {
            using var woCmd = new MySqlCommand(@"
                SELECT
                    wo.id,
                    wo.wo_number,
                    wo.status,
                    wip.created_at,
                    rs.step_number,
                    l.name AS current_location,
                    COALESCE(wip.current_step_id, 0) AS current_step_id,
                    COALESCE(wip.route_id, 0) AS wip_route_id
                FROM work_order wo
                LEFT JOIN wip_item wip ON wip.wo_order_id = wo.id
                LEFT JOIN route_step rs ON rs.id = wip.current_step_id
                LEFT JOIN location l ON l.id = rs.location_id
                WHERE wo.id = @workOrderId AND wo.product_id = @productId
                LIMIT 1;", cn);

            woCmd.Parameters.AddWithValue("@workOrderId", workOrderId.Value);
            woCmd.Parameters.AddWithValue("@productId", productId);

            using var rd = woCmd.ExecuteReader();
            if (rd.Read())
            {
                vm.WorkOrderId = rd.GetUInt32("id");
                vm.WorkOrderNumber = rd.GetString("wo_number");
                vm.WorkOrderStatus = rd.GetString("status");

                var createdAtOrdinal = rd.GetOrdinal("created_at");
                var stepOrdinal = rd.GetOrdinal("step_number");
                var currentLocationOrdinal = rd.GetOrdinal("current_location");
                var currentStepIdOrdinal = rd.GetOrdinal("current_step_id");
                var wipRouteIdOrdinal = rd.GetOrdinal("wip_route_id");

                vm.WipCreatedAt = rd.IsDBNull(createdAtOrdinal) ? null : rd.GetDateTime(createdAtOrdinal);
                vm.CurrentStepNumber = rd.IsDBNull(stepOrdinal) ? null : rd.GetUInt32("step_number");
                vm.CurrentLocation = rd.IsDBNull(currentLocationOrdinal) ? null : rd.GetString("current_location");

                if (!rd.IsDBNull(wipRouteIdOrdinal) && Convert.ToUInt32(rd["wip_route_id"]) != 0)
                {
                    routeId = Convert.ToUInt32(rd["wip_route_id"]);
                }

                if (!rd.IsDBNull(currentStepIdOrdinal) && Convert.ToUInt32(rd["current_step_id"]) != 0)
                {
                    var currentStepId = Convert.ToUInt32(rd["current_step_id"]);
                    vm.Steps = vm.Steps.Select(step =>
                    {
                        step.IsCurrent = step.StepId == currentStepId;
                        return step;
                    }).ToList();
                }
            }
        }

        if (routeId.HasValue)
        {
            using var stepsCmd = new MySqlCommand(@"
                SELECT rs.id, rs.step_number, l.name AS location_name
                FROM route_step rs
                JOIN location l ON l.id = rs.location_id
                WHERE rs.route_id = @routeId
                ORDER BY rs.step_number;", cn);

            stepsCmd.Parameters.AddWithValue("@routeId", routeId.Value);
            using var rd = stepsCmd.ExecuteReader();
            while (rd.Read())
            {
                vm.Steps.Add(new SearchRouteStepVm
                {
                    StepId = rd.GetUInt32("id"),
                    StepNumber = rd.GetUInt32("step_number"),
                    LocationName = rd.GetString("location_name"),
                    IsCurrent = vm.CurrentStepNumber.HasValue && vm.CurrentStepNumber.Value == rd.GetUInt32("step_number")
                });
            }
        }

        using (var relatedCmd = new MySqlCommand(@"
            SELECT
                wo.id AS work_order_id,
                p.id AS product_id,
                wo.wo_number,
                p.part_number,
                a.name AS area,
                f.name AS family,
                s.name AS subfamily,
                wo.status,
                l.name AS current_location
            FROM work_order wo
            JOIN product p ON p.id = wo.product_id
            JOIN subfamily s ON s.id = p.id_subfamily
            JOIN family f ON f.id = s.id_family
            JOIN area a ON a.id = f.id_area
            LEFT JOIN wip_item wip ON wip.wo_order_id = wo.id
            LEFT JOIN route_step rs ON rs.id = wip.current_step_id
            LEFT JOIN location l ON l.id = rs.location_id
            WHERE wo.product_id = @productId
            ORDER BY wo.id DESC
            LIMIT 6;", cn))
        {
            relatedCmd.Parameters.AddWithValue("@productId", productId);
            using var rd = relatedCmd.ExecuteReader();
            while (rd.Read())
            {
                var locationOrdinal = rd.GetOrdinal("current_location");
                vm.RelatedWorkOrders.Add(new SearchResultVm
                {
                    Type = "Work Order",
                    ProductId = rd.GetUInt32("product_id"),
                    WorkOrderId = rd.GetUInt32("work_order_id"),
                    PrimaryValue = rd.GetString("wo_number"),
                    SecondaryValue = rd.GetString("part_number"),
                    Area = rd.GetString("area"),
                    Family = rd.GetString("family"),
                    Subfamily = rd.GetString("subfamily"),
                    Status = rd.GetString("status"),
                    CurrentLocation = rd.IsDBNull(locationOrdinal) ? null : rd.GetString("current_location")
                });
            }
        }

        vm.Stats = new List<SearchDetailStatVm>
        {
            new() { Label = "Producto", Value = vm.PartNumber },
            new() { Label = "Área", Value = vm.Area },
            new() { Label = "Familia", Value = vm.Family },
            new() { Label = "Subfamilia", Value = vm.Subfamily },
            new() { Label = "Ruta", Value = $"{vm.RouteName} · v{vm.RouteVersion}" },
            new() { Label = "Estatus", Value = vm.WorkOrderId.HasValue ? vm.WorkOrderStatus : (vm.ProductActive ? "Producto activo" : "Producto inactivo") }
        };

        return vm;
    }
}
