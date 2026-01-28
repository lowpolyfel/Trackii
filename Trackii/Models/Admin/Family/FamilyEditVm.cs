using System.ComponentModel.DataAnnotations;

namespace Trackii.Models.Admin.Family;

public class FamilyEditVm
{
    public uint Id { get; set; }

    [Required]
    public uint AreaId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public bool Active { get; set; }
}
