namespace Trackii.Models.Reports;

public class ReworkReportVm : PagedReportVm
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public List<Row> Items { get; } = new();

    public class Row
    {
        public DateTime CreatedAt { get; set; }
        public string WoNumber { get; set; } = string.Empty;
        public uint WipItemId { get; set; }
        public string Location { get; set; } = string.Empty;
        public string User { get; set; } = string.Empty;
        public string Device { get; set; } = string.Empty;
        public int Qty { get; set; }
        public string? Reason { get; set; }
    }
}
