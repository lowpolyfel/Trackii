namespace Trackii.Models.Admin.Route;

public class RouteDeactivateVm
{
    public uint RouteId { get; set; }
    public string RouteName { get; set; } = string.Empty;
    public uint SubfamilyId { get; set; }
    public string SubfamilyName { get; set; } = string.Empty;
    public uint? ReplacementRouteId { get; set; }
    public List<Option> Options { get; set; } = [];

    public class Option
    {
        public uint Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public bool Active { get; set; }
    }
}
