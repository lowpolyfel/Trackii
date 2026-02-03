namespace Trackii.Models.Admin.Route;

public class RouteViewVm
{
    public uint Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Subfamily { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public bool Active { get; set; }
    public List<RouteStepVm> Steps { get; set; } = new();
}
