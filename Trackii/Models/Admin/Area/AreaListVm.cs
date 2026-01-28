namespace Trackii.Models.Admin.Area;

public class AreaListVm
{
    public List<AreaRowVm> Items { get; set; } = new();

    public string? Search { get; set; }
    public bool ShowInactive { get; set; }

    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalRows { get; set; }

    public int TotalPages =>
        (int)Math.Ceiling((double)TotalRows / PageSize);
}
