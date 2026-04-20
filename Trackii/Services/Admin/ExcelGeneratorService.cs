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

    public ExcelGeneratorVm GetPreview(int previewCount = 10)
    {
        var rows = GetRows();
        var maxSteps = rows.Count == 0 ? 0 : rows.Max(r => r.RouteSteps.Count);

        return new ExcelGeneratorVm
        {
            TotalRows = rows.Count,
            MaxSteps = maxSteps,
            Headers = BuildHeaders(maxSteps),
            PreviewRows = rows.Take(previewCount).ToList()
        };
    }

    public byte[] BuildExcelFile()
    {
        var rows = GetRows();
        var maxSteps = rows.Count == 0 ? 0 : rows.Max(r => r.RouteSteps.Count);
        var headers = BuildHeaders(maxSteps);

        using var workbook = new XLWorkbook();
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

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
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
        while (rd.Read())
        {
            var productId = rd.GetUInt32("product_id");

            if (!rows.TryGetValue(productId, out var row))
            {
                row = new ExcelGeneratorRowVm
                {
                    Product = rd.GetString("product"),
                    Family = rd.GetString("family"),
                    Subfamily = rd.GetString("subfamily")
                };
                rows.Add(productId, row);
            }

            if (!rd.IsDBNull("step_number"))
            {
                row.RouteSteps.Add(rd.IsDBNull("step_location")
                    ? $"Paso {rd.GetInt32("step_number")}"
                    : rd.GetString("step_location"));
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
}
