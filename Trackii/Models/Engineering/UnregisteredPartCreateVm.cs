using System.ComponentModel.DataAnnotations;

namespace Trackii.Models.Engineering;

public class UnregisteredPartCreateVm
{
    [Required]
    [Display(Name = "Part Number")]
    public string PartNumber { get; set; } = string.Empty;

    [Display(Name = "Fecha de registro")]
    public DateTime? CreatedAt { get; set; }
}
