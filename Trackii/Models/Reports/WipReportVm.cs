namespace Trackii.Models.Reports;

public class WipReportVm : PagedReportVm
{
    public string? Status { get; set; }
    public List<Row> Items { get; } = new();

    public class Row
    {
        public uint WipItemId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string WoNumber { get; set; } = string.Empty;
        public string? Location { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
