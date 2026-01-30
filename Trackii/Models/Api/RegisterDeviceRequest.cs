namespace Trackii.Models.Api;

public class RegisterDeviceRequest
{
    public string DeviceUid { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public uint LocationId { get; set; }
    public string Password { get; set; } = "";
}
