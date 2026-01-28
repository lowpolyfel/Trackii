using System.ComponentModel.DataAnnotations;

namespace Trackii.Models.Admin.Location;

public class LocationEditVm
{
    public uint Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = "";
}
