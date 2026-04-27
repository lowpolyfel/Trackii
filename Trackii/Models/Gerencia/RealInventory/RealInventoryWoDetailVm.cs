namespace Trackii.Models.Gerencia.RealInventory;

public class RealInventoryWoDetailVm
{
    public string WoNumber { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string Family { get; set; } = string.Empty;
    public string Subfamily { get; set; } = string.Empty;
    public string WoStatus { get; set; } = string.Empty;
    public string WipStatus { get; set; } = string.Empty;
    public DateTime? FirstStepAt { get; set; }
    public DateTime? LastStepAt { get; set; }
    public int TotalQtyIn { get; set; }
    public int TotalQtyScrap { get; set; }
    public int DaysSinceFirstStep { get; set; }
    public int DaysSinceLastStep { get; set; }
    public string? ReturnLocation { get; set; }
    public string? ReturnFamilyGroup { get; set; }
    public List<RealInventoryWoRouteStepVm> RouteSteps { get; } = new();
}

public class RealInventoryWoRouteStepVm
{
    public uint RouteStepId { get; set; }
    public int StepNumber { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public int QtyIn { get; set; }
    public int QtyScrap { get; set; }
    public DateTime? LastExecutionAt { get; set; }
    public List<RealInventoryWoStepEventVm> Events { get; } = new();
}

public class RealInventoryWoStepEventVm
{
    public DateTime CreatedAt { get; set; }
    public string EventType { get; set; } = string.Empty;
    public int Qty { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorDescription { get; set; }
    public string? Comments { get; set; }
    public string? UserName { get; set; }
}
