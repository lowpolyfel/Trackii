namespace Trackii.Models.Engineering.OrderTools;

public class OrderActivationVm
{
    public string? Search { get; set; }
    public List<OrderActivationRowVm> Items { get; set; } = new();
}

public class OrderActivationRowVm
{
    public uint WorkOrderId { get; set; }
    public string WoNumber { get; set; } = string.Empty;
    public string PartNumber { get; set; } = "-";
    public string Status { get; set; } = string.Empty;
    public bool Active { get; set; }
}
