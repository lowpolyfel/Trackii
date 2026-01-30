namespace Trackii.Models.Api;

public class WipCancelRequest
{
    public string Lot { get; set; } = "";
    public string PartNumber { get; set; } = "";
    public uint DeviceId { get; set; }
    public string? Reason { get; set; }
}

public class WipCancelResponse
{
    public bool Ok { get; set; }
    public string Status { get; set; } = "";
    public string? Reason { get; set; }
}
