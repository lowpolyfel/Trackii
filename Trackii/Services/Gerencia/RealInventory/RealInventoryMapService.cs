using MySql.Data.MySqlClient;
using Trackii.Models.Gerencia.RealInventory;

namespace Trackii.Services.Gerencia.RealInventory;

public class RealInventoryMapService
{
    private static readonly string[] FixedColumns =
    {
        "LATERAL LED", "LATERAL SENSOR", "LATERAL OPB",
        "MINI AXIAL", "MINI AXIAL OPB", "MAXI AXIAL",
        "FOTOLOGICO", "PHOTO OPBS"
    };

    private static readonly string[] FixedLocations =
    {
        "Alloy", "Backfill", "Moldeo", "FAST CAST", "Inspeccion Final",
        "Tie bar", "Tin plate", "Prueba Electrica", "Empaque", "QC", "Almacen"
    };

    private readonly string _conn;

    public RealInventoryMapService(IConfiguration cfg)
    {
        _conn = cfg.GetConnectionString("TrackiiDb") ?? throw new Exception("DB Connection missing");
    }

    public RealInventoryMapVm GetMap()
    {
        var vm = new RealInventoryMapVm
        {
            SnapshotAtUtc = DateTime.UtcNow,
            DataCutoffUtc = DateTime.UtcNow
        };

        vm.Columns.AddRange(FixedColumns);
        foreach (var location in FixedLocations)
        {
            var row = new RealInventoryLocationRowVm { LocationName = location };
            foreach (var column in FixedColumns)
            {
                row.PiecesByColumn[column] = 0;
            }

            vm.Rows.Add(row);
        }

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand($@"
            SELECT
                {BuildDestinationLocationSql("next_rs", "destination_l", "executed_l")} AS normalized_location,
                {BuildFamilyGroupSql()} AS family_group,
                COALESCE(SUM(COALESCE(last_qty.qty_in, 0)), 0) AS qty
            FROM wip_item wip
            JOIN work_order wo ON wo.id = wip.wo_order_id
            JOIN product p ON p.id = wo.product_id
            JOIN subfamily sf ON sf.id = p.id_subfamily
            JOIN family f ON f.id = sf.id_family
            JOIN (
                SELECT wse.wip_item_id, wse.route_step_id, wse.qty_in, wse.create_at
                FROM wip_step_execution wse
                INNER JOIN (
                    SELECT wip_item_id, MAX(id) AS max_wse_id
                    FROM wip_step_execution
                    GROUP BY wip_item_id
                ) latest ON latest.max_wse_id = wse.id
            ) last_qty ON last_qty.wip_item_id = wip.id
            LEFT JOIN route_step executed_rs ON executed_rs.id = last_qty.route_step_id
            LEFT JOIN location executed_l ON executed_l.id = executed_rs.location_id
            LEFT JOIN route_step next_rs
                ON next_rs.route_id = executed_rs.route_id
               AND next_rs.step_number = executed_rs.step_number + 1
            LEFT JOIN location destination_l ON destination_l.id = next_rs.location_id
            WHERE wo.active = 1
              AND (
                    (
                        wo.status IN ('OPEN', 'IN_PROGRESS', 'HOLD')
                        AND wip.status = 'ACTIVE'
                    )
                    OR
                    (
                        wo.status IN ('OPEN', 'IN_PROGRESS', 'HOLD', 'FINISHED')
                        AND wip.status = 'FINISHED'
                        AND next_rs.id IS NULL
                        AND {BuildIsQcLocationSql("executed_l")}
                    )
                )
            GROUP BY normalized_location, family_group", cn);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            if (rd.IsDBNull(rd.GetOrdinal("normalized_location")) || rd.IsDBNull(rd.GetOrdinal("family_group")))
            {
                continue;
            }

            var location = rd.GetString("normalized_location").Trim();
            var familyGroup = rd.GetString("family_group").Trim();

            if (!FixedLocations.Contains(location, StringComparer.OrdinalIgnoreCase) ||
                !FixedColumns.Contains(familyGroup, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var qty = Convert.ToInt32(rd["qty"]);
            var row = vm.Rows.FirstOrDefault(x => x.LocationName.Equals(location, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                continue;
            }

            row.PiecesByColumn[familyGroup] += qty;
        }

        return vm;
    }

    public RealInventoryCellDetailVm GetCellDetail(string location, string familyGroup)
    {
        var vm = new RealInventoryCellDetailVm
        {
            Location = location.Trim(),
            FamilyGroup = familyGroup.Trim()
        };

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand($@"
            SELECT
                q.wo_number,
                q.part_number,
                q.family_name,
                q.subfamily_name,
                q.normalized_location,
                q.current_qty,
                q.wo_status,
                q.wip_status,
                q.last_movement_at
            FROM (
                SELECT
                    wo.wo_number,
                    p.part_number,
                    COALESCE(f.name, 'Sin familia') AS family_name,
                    COALESCE(sf.name, 'Sin subfamilia') AS subfamily_name,
                    {BuildDestinationLocationSql("next_rs", "destination_l", "executed_l")} AS normalized_location,
                    {BuildFamilyGroupSql()} AS family_group,
                    COALESCE(last_qty.qty_in, 0) AS current_qty,
                    wo.status AS wo_status,
                    wip.status AS wip_status,
                    last_qty.create_at AS last_movement_at
                FROM wip_item wip
                JOIN work_order wo ON wo.id = wip.wo_order_id
                JOIN product p ON p.id = wo.product_id
                JOIN subfamily sf ON sf.id = p.id_subfamily
                JOIN family f ON f.id = sf.id_family
                JOIN (
                    SELECT wse.wip_item_id, wse.route_step_id, wse.qty_in, wse.create_at
                    FROM wip_step_execution wse
                    INNER JOIN (
                        SELECT wip_item_id, MAX(id) AS max_wse_id
                        FROM wip_step_execution
                        GROUP BY wip_item_id
                    ) latest ON latest.max_wse_id = wse.id
                ) last_qty ON last_qty.wip_item_id = wip.id
                LEFT JOIN route_step executed_rs ON executed_rs.id = last_qty.route_step_id
                LEFT JOIN location executed_l ON executed_l.id = executed_rs.location_id
                LEFT JOIN route_step next_rs
                    ON next_rs.route_id = executed_rs.route_id
                   AND next_rs.step_number = executed_rs.step_number + 1
                LEFT JOIN location destination_l ON destination_l.id = next_rs.location_id
                WHERE wo.active = 1
                  AND (
                        (
                            wo.status IN ('OPEN', 'IN_PROGRESS', 'HOLD')
                            AND wip.status = 'ACTIVE'
                        )
                        OR
                        (
                            wo.status IN ('OPEN', 'IN_PROGRESS', 'HOLD', 'FINISHED')
                            AND wip.status = 'FINISHED'
                            AND next_rs.id IS NULL
                            AND {BuildIsQcLocationSql("executed_l")}
                        )
                    )
            ) q
            WHERE q.normalized_location = @location
              AND q.family_group = @familyGroup
            ORDER BY q.last_movement_at DESC, q.wo_number ASC", cn);

        cmd.Parameters.AddWithValue("@location", vm.Location);
        cmd.Parameters.AddWithValue("@familyGroup", vm.FamilyGroup);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var lastMovementOrdinal = rd.GetOrdinal("last_movement_at");

            vm.Orders.Add(new RealInventoryOrderRowVm
            {
                WoNumber = rd.GetString("wo_number"),
                Product = rd.GetString("part_number"),
                Family = rd.GetString("family_name"),
                Subfamily = rd.GetString("subfamily_name"),
                CurrentLocation = rd.GetString("normalized_location"),
                Qty = Convert.ToInt32(rd.GetInt64("current_qty")),
                WoStatus = rd.GetString("wo_status"),
                WipStatus = rd.GetString("wip_status"),
                LastMovementAt = rd.IsDBNull(lastMovementOrdinal) ? null : rd.GetDateTime(lastMovementOrdinal)
            });
        }

        return vm;
    }

    private static string BuildDestinationLocationSql(string nextRouteStepAlias, string destinationLocationAlias, string executedLocationAlias)
    {
        return $@"
            CASE
                WHEN {nextRouteStepAlias}.id IS NOT NULL THEN {BuildNormalizedLocationSql(destinationLocationAlias, "NULL")}
                WHEN {nextRouteStepAlias}.id IS NULL AND {BuildIsQcLocationSql(executedLocationAlias)} THEN 'Almacen'
                ELSE NULL
            END";
    }

    private static string BuildIsQcLocationSql(string locationAlias)
    {
        return $@"(
            COALESCE({locationAlias}.name, '') LIKE '%QC%'
            OR COALESCE({locationAlias}.name, '') LIKE '%Q.C.%'
            OR COALESCE({locationAlias}.name, '') LIKE '%Calidad%'
        )";
    }

    private static string BuildNormalizedLocationSql(string locationAlias, string elseValue = "'Almacen'")
    {
        return $@"
            CASE
                WHEN COALESCE({locationAlias}.name, '') LIKE '%Alloy%' THEN 'Alloy'
                WHEN {locationAlias}.id = 8 OR COALESCE({locationAlias}.name, '') LIKE '%Backfill%' THEN 'Backfill'
                WHEN COALESCE({locationAlias}.name, '') LIKE '%Molde%' THEN 'Moldeo'
                WHEN COALESCE({locationAlias}.name, '') LIKE '%Fast%' THEN 'FAST CAST'
                WHEN COALESCE({locationAlias}.name, '') LIKE '%Inspec%' THEN 'Inspeccion Final'
                WHEN COALESCE({locationAlias}.name, '') LIKE '%Tie%' THEN 'Tie bar'
                WHEN COALESCE({locationAlias}.name, '') LIKE '%Tin%' THEN 'Tin plate'
                WHEN COALESCE({locationAlias}.name, '') LIKE '%Prueba%' THEN 'Prueba Electrica'
                WHEN COALESCE({locationAlias}.name, '') LIKE '%Emp%' THEN 'Empaque'
                WHEN COALESCE({locationAlias}.name, '') LIKE '%QC%'
                    OR COALESCE({locationAlias}.name, '') LIKE '%Q.C.%'
                    OR COALESCE({locationAlias}.name, '') LIKE '%Calidad%' THEN 'QC'
                WHEN COALESCE({locationAlias}.name, '') LIKE '%Almacen%'
                    OR COALESCE({locationAlias}.name, '') LIKE '%Almacén%' THEN 'Almacen'
                ELSE {elseValue}
            END";
    }

    private static string BuildFamilyGroupSql()
    {
        return @"
            CASE
                WHEN sf.id = 10 OR UPPER(COALESCE(sf.name, '')) = 'LATERAL OPB' THEN 'LATERAL OPB'
                WHEN sf.id = 3 OR UPPER(COALESCE(sf.name, '')) = 'MINI AXIAL OPB' THEN 'MINI AXIAL OPB'
                WHEN sf.id = 13 OR UPPER(COALESCE(sf.name, '')) = 'PHOTO OPBS' THEN 'PHOTO OPBS'
                WHEN f.id = 4 OR UPPER(COALESCE(f.name, '')) LIKE '%LATERAL LED%' THEN 'LATERAL LED'
                WHEN f.id = 3 OR UPPER(COALESCE(f.name, '')) LIKE '%LATERAL%SENSOR%' THEN 'LATERAL SENSOR'
                WHEN f.id = 2 OR UPPER(COALESCE(f.name, '')) LIKE '%MINI%AXIAL%' THEN 'MINI AXIAL'
                WHEN f.id = 1 OR UPPER(COALESCE(f.name, '')) LIKE '%MAXI%AXIAL%' THEN 'MAXI AXIAL'
                WHEN f.id = 5
                    OR UPPER(COALESCE(f.name, '')) LIKE '%FOTOLOGICO%'
                    OR UPPER(COALESCE(f.name, '')) LIKE '%FOTOLOGICOS%' THEN 'FOTOLOGICO'
                ELSE NULL
            END";
    }
}
