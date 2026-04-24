using MySql.Data.MySqlClient;
using Trackii.Models.Gerencia;

namespace Trackii.Services;

public class InventoryMapService
{
    private readonly string _conn;

    public InventoryMapService(IConfiguration cfg)
    {
        _conn = cfg.GetConnectionString("TrackiiDb") ?? throw new Exception("DB Connection missing");
    }

    public GerenciaBackendLobbyVm GetRealInventoryMap()
    {
        var vm = new GerenciaBackendLobbyVm();

        string[] columns =
        {
        "LATERAL LED", "LATERAL SENSOR", "LATERAL OPB",
        "MINI AXIAL", "MINI AXIAL OPB", "MAXI AXIAL",
        "FOTOLOGICO", "PHOTO OPBS"
    };
        vm.Columns.AddRange(columns);

        string[] locations = { "Alloy", "Backfill", "Moldeo", "FAST CAST",
        "Inspeccion Final", "Tie bar", "Tin plate", "Prueba Electrica",
        "Empaque", "QC", "Almacen" };

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

        var sql = @"
        SELECT
            CASE
                WHEN COALESCE(l.name, '') LIKE '%Alloy%' THEN 'Alloy'
                WHEN l.id = 8 OR COALESCE(l.name, '') LIKE '%Backfill%' THEN 'Backfill'
                WHEN COALESCE(l.name, '') LIKE '%Molde%' THEN 'Moldeo'
                WHEN COALESCE(l.name, '') LIKE '%Fast%' THEN 'FAST CAST'
                WHEN COALESCE(l.name, '') LIKE '%Inspec%' THEN 'Inspeccion Final'
                WHEN COALESCE(l.name, '') LIKE '%Tie%' THEN 'Tie bar'
                WHEN COALESCE(l.name, '') LIKE '%Tin%' THEN 'Tin plate'
                WHEN COALESCE(l.name, '') LIKE '%Prueba%' THEN 'Prueba Electrica'
                WHEN COALESCE(l.name, '') LIKE '%Emp%' THEN 'Empaque'
                WHEN COALESCE(l.name, '') LIKE '%QC%' OR COALESCE(l.name, '') LIKE '%Calidad%' THEN 'QC'
                WHEN COALESCE(l.name, '') LIKE '%Almacen%' THEN 'Almacen'
                ELSE 'Sin localidad'
            END AS normalized_location,
            CASE
                WHEN UPPER(f.name) LIKE '%LATERAL LED%' THEN 'LATERAL LED'
                -- LATERAL OPB: busca en familia O en subfamilia
                WHEN UPPER(f.name) LIKE '%LATERAL%' AND UPPER(COALESCE(sf.name, '')) LIKE '%OPB%' THEN 'LATERAL OPB'
                -- LATERAL SENSOR: familia LATERAL + sensor sin OPB
                WHEN UPPER(f.name) LIKE '%LATERAL%' AND UPPER(COALESCE(sf.name, '')) LIKE '%SENSOR%' 
                     AND UPPER(COALESCE(sf.name, '')) NOT LIKE '%OPB%' THEN 'LATERAL SENSOR'
                -- MINI AXIAL OPB
                WHEN UPPER(f.name) LIKE '%MINI%AXIAL%' AND UPPER(COALESCE(sf.name, '')) LIKE '%OPB%' THEN 'MINI AXIAL OPB'
                -- MINI AXIAL sin OPB
                WHEN UPPER(f.name) LIKE '%MINI%AXIAL%' AND UPPER(COALESCE(sf.name, '')) NOT LIKE '%OPB%' THEN 'MINI AXIAL'
                -- MAXI AXIAL
                WHEN UPPER(f.name) LIKE '%MAXI%AXIAL%' THEN 'MAXI AXIAL'
                -- PHOTO OPB (variante de FOTOLOGICO)
                WHEN UPPER(f.name) LIKE '%FOTOLOGICO%' AND UPPER(COALESCE(sf.name, '')) LIKE '%OPB%' THEN 'PHOTO OPBS'
                -- FOTOLOGICO sin OPB
                WHEN UPPER(f.name) LIKE '%FOTOLOGICO%' AND UPPER(COALESCE(sf.name, '')) NOT LIKE '%OPB%' THEN 'FOTOLOGICO'
                -- Fallback por subfamilia como último recurso
                WHEN UPPER(COALESCE(sf.name, '')) LIKE '%LATERAL%OPB%' THEN 'LATERAL OPB'
                WHEN UPPER(COALESCE(sf.name, '')) LIKE '%MINI%OPB%' THEN 'MINI AXIAL OPB'
                WHEN UPPER(COALESCE(sf.name, '')) LIKE '%FOTO%OPB%' THEN 'PHOTO OPBS'
                ELSE NULL
            END AS inventory_column,
            COALESCE(SUM(last_qty.qty_in), 0) AS qty
        FROM wip_item wip
        JOIN work_order wo ON wo.id = wip.wo_order_id
        JOIN product p ON p.id = wo.product_id
        JOIN subfamily sf ON sf.id = p.id_subfamily
        JOIN family f ON f.id = sf.id_family
        LEFT JOIN route_step rs ON rs.id = wip.current_step_id
        LEFT JOIN location l ON l.id = rs.location_id
        LEFT JOIN (
            SELECT wse.wip_item_id, wse.qty_in
            FROM wip_step_execution wse
            INNER JOIN (SELECT wip_item_id, MAX(id) as mid FROM wip_step_execution GROUP BY wip_item_id) m 
                ON m.mid = wse.id
        ) last_qty ON last_qty.wip_item_id = wip.id
        WHERE wo.status IN ('OPEN', 'IN_PROGRESS', 'FINISHED', 'HOLD')
          AND wip.status IN ('ACTIVE', 'FINISHED', 'HOLD')
        GROUP BY normalized_location, inventory_column";

        using var cmd = new MySqlCommand(sql, cn);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            if (rd.IsDBNull(rd.GetOrdinal("inventory_column")))
                continue;

            var locName = rd.GetString("normalized_location");
            var colName = rd.GetString("inventory_column");
            var qty = Convert.ToInt32(rd["qty"]);

            var row = vm.Rows.FirstOrDefault(r => r.LocationName == locName);
            if (row != null && row.PiecesByColumn.ContainsKey(colName))
            {
                row.PiecesByColumn[colName] += qty;
            }
        }

        return vm;
    }
}
