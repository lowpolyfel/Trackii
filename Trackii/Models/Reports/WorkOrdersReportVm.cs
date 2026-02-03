namespace Trackii.Models.Reports;

public class WorkOrdersReportVm : PagedReportVm
{
    public string? Status { get; set; }
    public List<Row> Items { get; } = new();

    public class Row
    {
        public string WoNumber { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Product { get; set; } = string.Empty;
        public string? CurrentLocation { get; set; }
        public DateTime? WipCreatedAt { get; set; }
    }
}
