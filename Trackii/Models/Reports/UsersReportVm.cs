namespace Trackii.Models.Reports;

public class UsersReportVm : PagedReportVm
{
    public bool OnlyActive { get; set; } = true;
    public List<Row> Items { get; } = new();

    public class Row
    {
        public string Username { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public bool Active { get; set; }
    }
}
