using System.ComponentModel.DataAnnotations;

namespace Trackii.Models.Admin.Role;

public class RoleEditVm
{
    public uint Id { get; set; }

    [Required, MaxLength(50)]
    public string Name { get; set; } = "";
}
