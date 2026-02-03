namespace Trackii.Models.Reports;

public class ProductsByAreaReportVm : PagedReportVm
{
    public List<Row> Items { get; } = new();

    public class Row
    {
        public string Area { get; set; } = string.Empty;
        public int Products { get; set; }
        public int ActiveProducts { get; set; }
    }
}
