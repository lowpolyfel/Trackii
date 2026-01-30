namespace Trackii.Models.Api;

public class WoQuickStatusResponse
{
    public string WoNumber { get; set; } = "";
    public bool HasWip { get; set; }
    public string Status { get; set; } = "";   // NONE, ACTIVE, FINISHED, SCRAPPED
    public uint? CurrentStep { get; set; }
    public string? ExpectedLocation { get; set; }
    public uint? QtyMax { get; set; }
}
