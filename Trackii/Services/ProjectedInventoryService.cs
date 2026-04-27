using MySql.Data.MySqlClient;
using Trackii.Models.Gerencia;

namespace Trackii.Services;

public class ProjectedInventoryService
{
    private readonly string _conn;

    public ProjectedInventoryService(IConfiguration cfg)
    {
        _conn = cfg.GetConnectionString("TrackiiDb") ?? throw new Exception("DB Connection missing");
    }

    public GerenciaBackendLobbyVm GetProjectedInventoryMap()
    {
        var vm = new GerenciaBackendLobbyVm();

        string[] columns = {
            "LATERAL LED", "LATERAL SENSOR", "LATERAL OPB",
            "MINI AXIAL", "MINI AXIAL OPB", "MAXI AXIAL",
            "FOTOLOGICO", "PHOTO OPBS"
        };
        vm.Columns.AddRange(columns);

        string[] locations = { "Alloy", "Backfill", "Moldeo", "FAST CAST", "Inspeccion Final", "Tie bar", "Tin plate", "Prueba Electrica", "Empaque", "QC", "Almacen" };

        foreach (var loc in locations)
        {
            var row = new BackendLobbyLocationRowVm { LocationName = loc };
            foreach (var col in columns)
            {
                row.PiecesByColumn[col] = 0;
            }
            vm.Rows.Add(row);
        }

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        string sql = @"
            SELECT
                CASE
                    WHEN next_rs.id IS NULL THEN 'Almacen'

                    WHEN COALESCE(next_loc.name, '') LIKE '%Alloy%' THEN 'Alloy'
                    WHEN next_loc.id = 8 OR COALESCE(next_loc.name, '') LIKE '%Backfill%' THEN 'Backfill'
                    WHEN COALESCE(next_loc.name, '') LIKE '%Molde%' THEN 'Moldeo'
                    WHEN COALESCE(next_loc.name, '') LIKE '%Fast%' THEN 'FAST CAST'
                    WHEN COALESCE(next_loc.name, '') LIKE '%Inspec%' THEN 'Inspeccion Final'
                    WHEN COALESCE(next_loc.name, '') LIKE '%Tie%' THEN 'Tie bar'
                    WHEN COALESCE(next_loc.name, '') LIKE '%Tin%' THEN 'Tin plate'
                    WHEN COALESCE(next_loc.name, '') LIKE '%Prueba%' THEN 'Prueba Electrica'
                    WHEN COALESCE(next_loc.name, '') LIKE '%Emp%' THEN 'Empaque'
                    WHEN COALESCE(next_loc.name, '') LIKE '%QC%' OR COALESCE(next_loc.name, '') LIKE '%Calidad%' THEN 'QC'
                    WHEN COALESCE(next_loc.name, '') LIKE '%Almacen%' THEN 'Almacen'
                    ELSE 'Sin localidad'
                END AS projected_location,

              CASE
    -- OPB primero, porque en tu BD LATERAL OPB pertenece a la familia LATERAL LED
    WHEN sf.id = 10 OR UPPER(COALESCE(sf.name, '')) = 'LATERAL OPB' THEN 'LATERAL OPB'

    WHEN sf.id = 3 OR UPPER(COALESCE(sf.name, '')) = 'MINI AXIAL OPB' THEN 'MINI AXIAL OPB'

    WHEN sf.id = 13 OR UPPER(COALESCE(sf.name, '')) = 'PHOTO OPBS' THEN 'PHOTO OPBS'

    -- Después las familias normales
    WHEN f.id = 4 OR UPPER(f.name) LIKE '%LATERAL LED%' THEN 'LATERAL LED'

    WHEN f.id = 3 OR UPPER(f.name) LIKE '%LATERAL%SENSOR%' THEN 'LATERAL SENSOR'

    WHEN f.id = 2 OR UPPER(f.name) LIKE '%MINI%AXIAL%' THEN 'MINI AXIAL'

    WHEN f.id = 1 OR UPPER(f.name) LIKE '%MAXI%AXIAL%' THEN 'MAXI AXIAL'

    WHEN f.id = 5 OR UPPER(f.name) LIKE '%FOTOLOGICO%' OR UPPER(f.name) LIKE '%FOTOLOGICOS%' THEN 'FOTOLOGICO'

    ELSE NULL
END AS inventory_column,

                COALESCE(SUM(last_qty.qty_in), 0) AS qty
            FROM wip_item wip
            JOIN work_order wo ON wo.id = wip.wo_order_id
            JOIN product p ON p.id = wo.product_id
            JOIN subfamily sf ON sf.id = p.id_subfamily
            JOIN family f ON f.id = sf.id_family

            LEFT JOIN route_step curr_rs ON curr_rs.id = wip.current_step_id

            LEFT JOIN route_step next_rs ON next_rs.route_id = curr_rs.route_id AND next_rs.step_number = (curr_rs.step_number + 1)

            LEFT JOIN location next_loc ON next_loc.id = next_rs.location_id

            LEFT JOIN (
                SELECT wse.wip_item_id, wse.qty_in
                FROM wip_step_execution wse
                INNER JOIN (SELECT wip_item_id, MAX(id) as mid FROM wip_step_execution GROUP BY wip_item_id) m ON m.mid = wse.id
            ) last_qty ON last_qty.wip_item_id = wip.id

            WHERE wo.status IN ('OPEN', 'IN_PROGRESS', 'FINISHED', 'HOLD')
              AND wip.status IN ('ACTIVE', 'FINISHED', 'HOLD')
            GROUP BY projected_location, inventory_column";

        using var cmd = new MySqlCommand(sql, cn);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            if (rd.IsDBNull(rd.GetOrdinal("inventory_column")))
                continue;

            string locName = rd.GetString("projected_location");
            string colName = rd.GetString("inventory_column");
            int qty = Convert.ToInt32(rd["qty"]);

            var row = vm.Rows.FirstOrDefault(r => r.LocationName == locName);
            if (row != null && row.PiecesByColumn.ContainsKey(colName))
            {
                row.PiecesByColumn[colName] += qty;
            }
        }
        return vm;
    }
}
