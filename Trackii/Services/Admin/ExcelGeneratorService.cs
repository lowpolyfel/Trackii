using ClosedXML.Excel;
using MySql.Data.MySqlClient;
using Trackii.Models.Admin.ExcelGenerator;

namespace Trackii.Services.Admin;

public class ExcelGeneratorService
{
    private readonly string _conn;

    public ExcelGeneratorService(IConfiguration cfg)
    {
        _conn = cfg.GetConnectionString("TrackiiDb")
            ?? throw new Exception("Connection string TrackiiDb no configurada");
    }

    public ExcelGeneratorLandingVm GetLandingSummary()
    {
        return new ExcelGeneratorLandingVm
        {
            RoutesBySubfamilyTotalRows = GetRows().Count,
            ActiveOrdersTotalRows = GetActiveOrdersRows().Count
        };
    }

    public RoutesBySubfamilyExcelVm GetRoutesBySubfamilyPreview(int previewCount = 10)
    {
        var rows = GetRows();
        var maxSteps = rows.Count == 0 ? 0 : rows.Max(r => r.RouteSteps.Count);

        return new RoutesBySubfamilyExcelVm
        {
            TotalRows = rows.Count,
            MaxSteps = maxSteps,
            Headers = BuildHeaders(maxSteps),
            PreviewRows = rows.Take(previewCount).ToList()
        };
    }

    public byte[] BuildRoutesBySubfamilyExcelFile()
    {
        var rows = GetRows();
        var maxSteps = rows.Count == 0 ? 0 : rows.Max(r => r.RouteSteps.Count);
        var headers = BuildHeaders(maxSteps);

        using var workbook = new XLWorkbook();
        BuildRoutesBySubfamilySheet(workbook, headers, rows, maxSteps);

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    public ActiveOrdersExcelVm GetActiveOrdersPreview(int previewCount = 30)
    {
        var rows = GetActiveOrdersRows();
        return new ActiveOrdersExcelVm
        {
            TotalRows = rows.Count,
            PreviewRows = rows.Take(previewCount).ToList()
        };
    }

    public byte[] BuildActiveOrdersExcelFile()
    {
        var rows = GetActiveOrdersRows();

        using var workbook = new XLWorkbook();
        BuildActiveOrdersSheet(workbook, rows);

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private static void BuildRoutesBySubfamilySheet(XLWorkbook workbook, List<string> headers, List<ExcelGeneratorRowVm> rows, int maxSteps)
    {
        var sheet = workbook.Worksheets.Add("Rutas por Subfamilia");

        for (var col = 0; col < headers.Count; col++)
        {
            sheet.Cell(1, col + 1).Value = headers[col];
        }

        var currentRow = 2;
        foreach (var item in rows)
        {
            sheet.Cell(currentRow, 1).Value = item.Product;
            sheet.Cell(currentRow, 2).Value = item.Family;
            sheet.Cell(currentRow, 3).Value = item.Subfamily;

            for (var stepIndex = 0; stepIndex < maxSteps; stepIndex++)
            {
                var value = stepIndex < item.RouteSteps.Count ? item.RouteSteps[stepIndex] : string.Empty;
                sheet.Cell(currentRow, stepIndex + 4).Value = value;
            }

            currentRow++;
        }

        var usedRange = sheet.Range(1, 1, Math.Max(1, currentRow - 1), headers.Count);
        var table = usedRange.CreateTable("RutasTable");
        table.Theme = XLTableTheme.TableStyleMedium2;

        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        usedRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        usedRange.Style.Alignment.WrapText = true;

        sheet.Columns().AdjustToContents(12, 42);
    }

    private static void BuildActiveOrdersSheet(XLWorkbook workbook, List<ActiveOrderExcelRowVm> rows)
    {
        var sheet = workbook.Worksheets.Add("Ordenes activas");

        var headers = new[]
        {
            "WO",
            "NO. PARTE",
            "ESTATUS ORDEN",
            "ESTATUS WIP",
            "LOCALIDAD CONGELADA",
            "CREACION WO",
            "ULTIMO MOVIMIENTO",
            "DIAS PARADA"
        };

        for (var col = 0; col < headers.Length; col++)
        {
            sheet.Cell(1, col + 1).Value = headers[col];
        }

        var currentRow = 2;
        foreach (var item in rows)
        {
            sheet.Cell(currentRow, 1).Value = item.WorkOrder;
            sheet.Cell(currentRow, 2).Value = item.PartNumber;
            sheet.Cell(currentRow, 3).Value = item.WorkOrderStatus;
            sheet.Cell(currentRow, 4).Value = item.WipStatus;
            sheet.Cell(currentRow, 5).Value = item.FrozenLocation;
            sheet.Cell(currentRow, 6).Value = item.CreatedAt;
            sheet.Cell(currentRow, 6).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
            sheet.Cell(currentRow, 7).Value = item.LastMovementAt;
            sheet.Cell(currentRow, 7).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
            sheet.Cell(currentRow, 8).Value = item.DaysStopped;
            currentRow++;
        }

        var usedRange = sheet.Range(1, 1, Math.Max(1, currentRow - 1), headers.Length);
        var table = usedRange.CreateTable("OrdenesActivasTable");
        table.Theme = XLTableTheme.TableStyleMedium9;

        sheet.Row(1).Style.Font.Bold = true;
        sheet.Row(1).Style.Font.FontColor = XLColor.White;
        sheet.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1E4ED8");
        sheet.SheetView.FreezeRows(1);
        usedRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        usedRange.Style.Alignment.WrapText = true;
        sheet.Columns().AdjustToContents(14, 42);
        usedRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        usedRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        usedRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#9DB4F0");
        usedRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#D4E0FF");

        if (rows.Count > 0)
        {
            var daysColumnRange = sheet.Range(2, 8, currentRow - 1, 8);
            daysColumnRange.Style.NumberFormat.Format = "0";
            daysColumnRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            daysColumnRange.AddConditionalFormat()
                .WhenGreaterThanOrEqual(10)
                .Fill.SetBackgroundColor(XLColor.FromHtml("#FECACA"))
                .Font.SetFontColor(XLColor.FromHtml("#991B1B"));

            daysColumnRange.AddConditionalFormat()
                .WhenBetween(5, 9)
                .Fill.SetBackgroundColor(XLColor.FromHtml("#FEF3C7"))
                .Font.SetFontColor(XLColor.FromHtml("#92400E"));

            var locationRange = sheet.Range(2, 5, currentRow - 1, 5);
            locationRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF4FF");
        }
    }

    private List<ExcelGeneratorRowVm> GetRows()
    {
        var rows = new Dictionary<uint, ExcelGeneratorRowVm>();

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(@"
            SELECT
                p.id AS product_id,
                p.part_number AS product,
                f.name AS family,
                sf.name AS subfamily,
                rs.step_number,
                l.name AS step_location
            FROM product p
            JOIN subfamily sf ON sf.id = p.id_subfamily
            JOIN family f ON f.id = sf.id_family
            LEFT JOIN route r ON r.id = sf.active_route_id
            LEFT JOIN route_step rs ON rs.route_id = r.id
            LEFT JOIN location l ON l.id = rs.location_id
            WHERE p.active = 1 AND sf.active = 1 AND f.active = 1
            ORDER BY p.part_number, rs.step_number
        ", cn);

        using var rd = cmd.ExecuteReader();

        var productIdOrdinal = rd.GetOrdinal("product_id");
        var productOrdinal = rd.GetOrdinal("product");
        var familyOrdinal = rd.GetOrdinal("family");
        var subfamilyOrdinal = rd.GetOrdinal("subfamily");
        var stepNumberOrdinal = rd.GetOrdinal("step_number");
        var stepLocationOrdinal = rd.GetOrdinal("step_location");

        while (rd.Read())
        {
            var productId = rd.GetUInt32(productIdOrdinal);

            if (!rows.TryGetValue(productId, out var row))
            {
                row = new ExcelGeneratorRowVm
                {
                    Product = rd.GetString(productOrdinal),
                    Family = rd.GetString(familyOrdinal),
                    Subfamily = rd.GetString(subfamilyOrdinal)
                };
                rows.Add(productId, row);
            }

            if (!rd.IsDBNull(stepNumberOrdinal))
            {
                row.RouteSteps.Add(rd.IsDBNull(stepLocationOrdinal)
                    ? $"Paso {rd.GetInt32(stepNumberOrdinal)}"
                    : rd.GetString(stepLocationOrdinal));
            }
        }

        return rows.Values.ToList();
    }

    private static List<string> BuildHeaders(int maxSteps)
    {
        var headers = new List<string>
        {
            "PRODUCTO",
            "FAMILIA",
            "SUBFAMILIA"
        };

        for (var i = 1; i <= maxSteps; i++)
        {
            headers.Add($"PASO {i} DE LA RUTA");
        }

        return headers;
    }

    private List<ActiveOrderExcelRowVm> GetActiveOrdersRows()
    {
        var rows = new List<ActiveOrderExcelRowVm>();

        using var cn = new MySqlConnection(_conn);
        cn.Open();

        using var cmd = new MySqlCommand(@"
            SELECT wo.wo_number,
                   wo.status AS work_order_status,
                   COALESCE(wip.status, 'SIN WIP') AS wip_status,
                   p.part_number,
                   COALESCE(l.name, 'Sin localidad') AS frozen_location,
                   COALESCE(wo.creation_datetime, NOW()) AS created_at,
                   COALESCE(last_exec.last_update_at, wip.created_at, wo.creation_datetime, NOW()) AS last_movement_at,
                   DATEDIFF(NOW(), COALESCE(last_exec.last_update_at, wip.created_at, wo.creation_datetime, NOW())) AS days_stopped
            FROM work_order wo
            JOIN product p ON p.id = wo.product_id
            LEFT JOIN wip_item wip ON wip.wo_order_id = wo.id
            LEFT JOIN route_step rs ON rs.id = wip.current_step_id
            LEFT JOIN location l ON l.id = rs.location_id
            LEFT JOIN (
                SELECT wse.wip_item_id,
                       MAX(wse.create_at) AS last_update_at
                FROM wip_step_execution wse
                GROUP BY wse.wip_item_id
            ) last_exec ON last_exec.wip_item_id = wip.id
            WHERE wo.status IN ('OPEN', 'IN_PROGRESS', 'HOLD')
            ORDER BY created_at ASC, wo.wo_number ASC
        ", cn);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            rows.Add(new ActiveOrderExcelRowVm
            {
                WorkOrder = rd.GetString("wo_number"),
                PartNumber = rd.GetString("part_number"),
                WorkOrderStatus = rd.GetString("work_order_status"),
                WipStatus = rd.GetString("wip_status"),
                FrozenLocation = rd.GetString("frozen_location"),
                CreatedAt = rd.GetDateTime("created_at"),
                LastMovementAt = rd.GetDateTime("last_movement_at"),
                DaysStopped = rd.GetInt32("days_stopped")
            });
        }

        return rows;
    }
}
