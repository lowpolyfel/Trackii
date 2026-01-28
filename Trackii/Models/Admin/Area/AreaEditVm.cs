using System.ComponentModel.DataAnnotations;

namespace Trackii.Models.Admin.Area;

public class AreaEditVm
{
    public uint Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public bool Active { get; set; }
}
