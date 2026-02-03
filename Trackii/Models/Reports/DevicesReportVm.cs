namespace Trackii.Models.Reports;

public class DevicesReportVm : PagedReportVm
{
    public bool OnlyActive { get; set; } = true;
    public List<Row> Items { get; } = new();

    public class Row
    {
        public string DeviceUid { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string Location { get; set; } = string.Empty;
        public bool Active { get; set; }
    }
}
