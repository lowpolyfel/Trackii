using System.ComponentModel.DataAnnotations;

namespace Trackii.Models.Admin.Product;

public class ProductEditVm
{
    public uint Id { get; set; }

    public uint? AreaId { get; set; }

    public uint? FamilyId { get; set; }

    [Required]
    public uint SubfamilyId { get; set; }

    [Required, MaxLength(50)]
    public string PartNumber { get; set; } = "";
}
