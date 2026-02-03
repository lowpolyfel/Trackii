namespace Trackii.Models.Reports;

public abstract class PagedReportVm
{
    public string? Search { get; set; }
    public int Page { get; set; }
    public int TotalPages { get; set; }
}
