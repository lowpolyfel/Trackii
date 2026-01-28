using System.ComponentModel.DataAnnotations;

namespace Trackii.Models.Admin.User;

public class UserEditVm
{
    public uint Id { get; set; }

    [Required]
    public string Username { get; set; } = "";

    [Required]
    public uint RoleId { get; set; }

    public bool Active { get; set; }

    // Solo para edición
    [DataType(DataType.Password)]
    public string? NewPassword { get; set; }
}
