namespace Trackii.Models.Api;

public class DeviceActivationRequest
{
    public string Token { get; set; } = "";
    public string AndroidId { get; set; } = "";
}

public class DeviceActivationResponse
{
    public bool Ok { get; set; }
    public string? Reason { get; set; }
    public uint DeviceId { get; set; }
    public string DeviceUid { get; set; } = "";
}
