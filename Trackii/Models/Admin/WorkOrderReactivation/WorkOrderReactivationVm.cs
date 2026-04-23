namespace Trackii.Models.Admin.WorkOrderReactivation;

public class WorkOrderReactivationVm
{
    public string? Search { get; set; }
    public List<WorkOrderReactivationRowVm> Items { get; set; } = new();
}

public class WorkOrderReactivationRowVm
{
    public uint WorkOrderId { get; set; }
    public string WoNumber { get; set; } = string.Empty;
    public string PartNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string CancelledAtStep { get; set; } = string.Empty;
}
