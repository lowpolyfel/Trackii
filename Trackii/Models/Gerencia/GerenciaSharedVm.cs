namespace Trackii.Models.Gerencia;

public class ChartVm
{
    public List<string> Labels { get; } = new();
    public List<int> Values { get; } = new();
}

public class LocationProductionVm
{
    public string Location { get; set; } = string.Empty;
    public int QtyProduced { get; set; }
}

public class WorkOrderVm
{
    public string WoNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public uint? WipItemId { get; set; }
    public string? WipStatus { get; set; }
    public string? CurrentLocation { get; set; }
    public DateTime? WipCreatedAt { get; set; }
}

public class DelayedWorkOrderVm : WorkOrderVm
{
    public int DaysDelayed { get; set; }
}
