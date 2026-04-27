using ClosedXML.Excel;
using MySql.Data.MySqlClient;

namespace Trackii.Services.Gerencia.RealInventory;

public class RealInventoryDiscreteExcelService
{
    private readonly string _conn;

    public RealInventoryDiscreteExcelService(IConfiguration cfg)
    {
        _conn = cfg.GetConnectionString("TrackiiDb") ?? throw new Exception("DB Connection missing");
    }

    public byte[] BuildExcel()
    {
        using var workbook = new XLWorkbook();
        BuildFamiliesSheet(workbook);
        BuildInventorySheet(workbook);

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private void BuildFamiliesSheet(XLWorkbook workbook)
    {
        var sheet = workbook.Worksheets.Add("Familias");
        var headers = new[] { "Familia/Subfamilia", "Piezas", "Órdenes activas (OPEN)", "Órdenes en proceso (IN_PROGRESS)" };
        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = headers[i];
        }

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand($@"
            SELECT
                {BuildFamilyGroupSql()} AS family_group,
                COALESCE(SUM(COALESCE(last_wse.qty_in, 0)), 0) AS piezas,
                COUNT(DISTINCT CASE WHEN wo.status = 'OPEN' THEN wo.id END) AS active_orders,
                COUNT(DISTINCT CASE WHEN wo.status = 'IN_PROGRESS' THEN wo.id END) AS in_progress_orders
            FROM wip_item wip
            JOIN work_order wo ON wo.id = wip.wo_order_id
            JOIN product p ON p.id = wo.product_id
            JOIN subfamily sf ON sf.id = p.id_subfamily
            JOIN family f ON f.id = sf.id_family
            JOIN (
                SELECT wse.wip_item_id, wse.qty_in
                FROM wip_step_execution wse
                JOIN (
                    SELECT wip_item_id, MAX(id) AS max_id
                    FROM wip_step_execution
                    GROUP BY wip_item_id
                ) latest ON latest.max_id = wse.id
            ) last_wse ON last_wse.wip_item_id = wip.id
            WHERE wo.active = 1
              AND wo.status IN ('OPEN', 'IN_PROGRESS', 'HOLD')
              AND wip.status IN ('ACTIVE', 'HOLD')
            GROUP BY family_group
            ORDER BY family_group", cn);

        using var rd = cmd.ExecuteReader();
        var row = 2;
        while (rd.Read())
        {
            if (rd.IsDBNull(rd.GetOrdinal("family_group")))
            {
                continue;
            }

            sheet.Cell(row, 1).Value = rd.GetString("family_group");
            sheet.Cell(row, 2).Value = Convert.ToInt32(rd.GetInt64("piezas"));
            sheet.Cell(row, 3).Value = Convert.ToInt32(rd.GetInt64("active_orders"));
            sheet.Cell(row, 4).Value = Convert.ToInt32(rd.GetInt64("in_progress_orders"));
            row++;
        }

        var range = sheet.Range(1, 1, Math.Max(row - 1, 1), headers.Length);
        var table = range.CreateTable("FamiliasDiscretos");
        table.Theme = XLTableTheme.TableStyleMedium2;
        table.ShowAutoFilter = true;
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents(14, 48);
    }

    private void BuildInventorySheet(XLWorkbook workbook)
    {
        var sheet = workbook.Worksheets.Add("Inventario");
        var headers = new[]
        {
            "WO", "No. Parte", "Familia", "Target QTY", "Qty actual", "Cantidad final",
            "Estado orden", "Scrap", "Días desde inicio", "Localidad actual"
        };
        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = headers[i];
        }

        using var cn = new MySqlConnection(_conn);
        cn.Open();
        using var cmd = new MySqlCommand($@"
            SELECT
                wo.wo_number,
                p.part_number,
                {BuildFamilyGroupSql()} AS family_group,
                COALESCE(first_wse.qty_in, 0) AS target_qty,
                GREATEST(COALESCE(last_wse.qty_in, 0) - COALESCE(scrap_total.qty_scrap, 0), 0) AS qty_actual,
                CASE
                    WHEN wo.status = 'FINISHED' OR wip.status = 'FINISHED'
                    THEN GREATEST(COALESCE(last_wse.qty_in, 0) - COALESCE(scrap_total.qty_scrap, 0), 0)
                    ELSE NULL
                END AS qty_final,
                wo.status AS order_status,
                COALESCE(scrap_total.qty_scrap, 0) AS qty_scrap,
                TIMESTAMPDIFF(DAY, first_wse.create_at, NOW()) AS days_since_start,
                {BuildNormalizedLocationSql("l")} AS current_location
            FROM wip_item wip
            JOIN work_order wo ON wo.id = wip.wo_order_id
            JOIN product p ON p.id = wo.product_id
            JOIN subfamily sf ON sf.id = p.id_subfamily
            JOIN family f ON f.id = sf.id_family
            LEFT JOIN route_step rs ON rs.id = wip.current_step_id
            LEFT JOIN location l ON l.id = rs.location_id
            LEFT JOIN (
                SELECT wse.wip_item_id, wse.qty_in, wse.create_at
                FROM wip_step_execution wse
                JOIN (
                    SELECT wip_item_id, MIN(id) AS min_id
                    FROM wip_step_execution
                    GROUP BY wip_item_id
                ) first_id ON first_id.min_id = wse.id
            ) first_wse ON first_wse.wip_item_id = wip.id
            LEFT JOIN (
                SELECT wse.wip_item_id, wse.qty_in
                FROM wip_step_execution wse
                JOIN (
                    SELECT wip_item_id, MAX(id) AS max_id
                    FROM wip_step_execution
                    GROUP BY wip_item_id
                ) last_id ON last_id.max_id = wse.id
            ) last_wse ON last_wse.wip_item_id = wip.id
            LEFT JOIN (
                SELECT wip_item_id, COALESCE(SUM(qty_scrap), 0) AS qty_scrap
                FROM wip_step_execution
                GROUP BY wip_item_id
            ) scrap_total ON scrap_total.wip_item_id = wip.id
            WHERE wo.active = 1
              AND wo.status IN ('OPEN', 'IN_PROGRESS', 'HOLD', 'FINISHED')
              AND wip.status IN ('ACTIVE', 'HOLD', 'FINISHED')
            ORDER BY wo.status, wo.wo_number", cn);

        using var rd = cmd.ExecuteReader();
        var row = 2;
        while (rd.Read())
        {
            sheet.Cell(row, 1).Value = rd.GetString("wo_number");
            sheet.Cell(row, 2).Value = rd.GetString("part_number");
            sheet.Cell(row, 3).Value = rd.IsDBNull(rd.GetOrdinal("family_group")) ? "N/A" : rd.GetString("family_group");
            sheet.Cell(row, 4).Value = Convert.ToInt32(rd.GetInt64("target_qty"));
            sheet.Cell(row, 5).Value = Convert.ToInt32(rd.GetInt64("qty_actual"));

            if (!rd.IsDBNull(rd.GetOrdinal("qty_final")))
            {
                sheet.Cell(row, 6).Value = Convert.ToInt32(rd.GetInt64("qty_final"));
            }

            sheet.Cell(row, 7).Value = rd.GetString("order_status");
            sheet.Cell(row, 8).Value = Convert.ToInt32(rd.GetInt64("qty_scrap"));
            sheet.Cell(row, 9).Value = rd.IsDBNull(rd.GetOrdinal("days_since_start")) ? 0 : rd.GetInt32("days_since_start");
            sheet.Cell(row, 10).Value = rd.GetString("current_location");
            row++;
        }

        var range = sheet.Range(1, 1, Math.Max(row - 1, 1), headers.Length);
        var table = range.CreateTable("InventarioDiscretos");
        table.Theme = XLTableTheme.TableStyleMedium9;
        table.ShowAutoFilter = true;
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents(12, 42);
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
                WHEN f.id = 5 OR UPPER(COALESCE(f.name, '')) LIKE '%FOTOLOGICO%' THEN 'FOTOLOGICO'
                ELSE NULL
            END";
    }

    private static string BuildNormalizedLocationSql(string locationAlias)
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
                WHEN COALESCE({locationAlias}.name, '') LIKE '%QC%' OR COALESCE({locationAlias}.name, '') LIKE '%Q.C.%' OR COALESCE({locationAlias}.name, '') LIKE '%Calidad%' THEN 'QC'
                WHEN COALESCE({locationAlias}.name, '') LIKE '%Almacen%' OR COALESCE({locationAlias}.name, '') LIKE '%Almacén%' THEN 'Almacen'
                ELSE COALESCE({locationAlias}.name, 'Sin localidad')
            END";
    }
}
