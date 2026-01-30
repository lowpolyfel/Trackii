using System.ComponentModel.DataAnnotations;

namespace Trackii.Models.Admin.Route;

public class RouteEditVm
{
    public uint Id { get; set; }

    public uint SubfamilyId { get; set; }

    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string Version { get; set; } = string.Empty;
    public bool Active { get; set; }

    // Lista ordenada (solo seleccionados)
    public List<RouteStepVm> Steps { get; set; } = new();

    public string? Error { get; set; }
}
