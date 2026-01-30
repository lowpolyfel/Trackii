namespace Trackii.Models.Api;

public class WipReworkRequest
{
    public string Lot { get; set; } = "";
    public string PartNumber { get; set; } = "";
    public uint DeviceId { get; set; }
    public uint LocationId { get; set; }
    public uint Qty { get; set; }
    public string? Reason { get; set; }
}

public class WipReworkReleaseRequest
{
    public string Lot { get; set; } = "";
    public string PartNumber { get; set; } = "";
}

public class WipReworkResponse
{
    public bool Ok { get; set; }
    public string Status { get; set; } = "";
    public string? Reason { get; set; }
}
