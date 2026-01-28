namespace Trackii.Models.Admin.Family;

public class FamilyListVm
{
    public List<FamilyRowVm> Items { get; set; } = new();

    public uint? AreaId { get; set; }
    public string? Search { get; set; }
    public bool ShowInactive { get; set; }

    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalRows { get; set; }

    public int TotalPages =>
        (int)Math.Ceiling((double)TotalRows / PageSize);

    public List<(uint Id, string Name)> Areas { get; set; } = new();
}
