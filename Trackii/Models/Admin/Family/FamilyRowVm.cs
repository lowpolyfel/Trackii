namespace Trackii.Models.Admin.Family;

public class FamilyRowVm
{
    public uint Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string AreaName { get; set; } = string.Empty;
    public bool Active { get; set; }
}
