namespace Trackii.Models.Reports;

public class ProductsReportVm : PagedReportVm
{
    public bool ShowInactive { get; set; }
    public List<Row> Items { get; } = new();

    public class Row
    {
        public string PartNumber { get; set; } = string.Empty;
        public string Area { get; set; } = string.Empty;
        public string Family { get; set; } = string.Empty;
        public string Subfamily { get; set; } = string.Empty;
        public bool Active { get; set; }
    }
}
