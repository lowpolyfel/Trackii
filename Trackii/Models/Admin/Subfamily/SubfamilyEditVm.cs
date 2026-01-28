using System.ComponentModel.DataAnnotations;

namespace Trackii.Models.Admin.Subfamily;

public class SubfamilyEditVm
{
    public uint Id { get; set; }

    [Required]
    public uint FamilyId { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = "";
}
