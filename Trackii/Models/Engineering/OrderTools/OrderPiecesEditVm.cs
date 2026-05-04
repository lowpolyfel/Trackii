namespace Trackii.Models.Engineering.OrderTools;

public class OrderPiecesEditVm
{
    public string? Search { get; set; }
    public string? SelectedWo { get; set; }
    public List<OrderLookupRowVm> Matches { get; set; } = new();
    public OrderPiecesDetailVm? Detail { get; set; }
}

public class OrderLookupRowVm
{
    public uint WorkOrderId { get; set; }
    public string WoNumber { get; set; } = string.Empty;
    public string PartNumber { get; set; } = "-";
}

public class OrderPiecesDetailVm
{
    public uint WorkOrderId { get; set; }
    public string WoNumber { get; set; } = string.Empty;
    public string PartNumber { get; set; } = "-";
    public List<OrderStepQtyVm> Steps { get; set; } = new();
}

public class OrderStepQtyVm
{
    public uint RouteStepId { get; set; }
    public int StepNumber { get; set; }
    public string Location { get; set; } = "Sin localidad";
    public int QtyIn { get; set; }
    public int QtyScrap { get; set; }
}
