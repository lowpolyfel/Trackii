using System.ComponentModel.DataAnnotations;

namespace Trackii.Models.Admin.Device;

public class DeviceEditVm
{
    public uint Id { get; set; }

    [Required, MaxLength(100)]
    public string DeviceUid { get; set; } = "";

    [MaxLength(100)]
    public string? Name { get; set; }

    [Required]
    public uint LocationId { get; set; }
}
