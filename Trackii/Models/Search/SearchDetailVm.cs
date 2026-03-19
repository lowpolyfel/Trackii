namespace Trackii.Models.Search;

public class SearchDetailVm
{
    public uint ProductId { get; set; }
    public uint? WorkOrderId { get; set; }
    public string PartNumber { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string Family { get; set; } = string.Empty;
    public string Subfamily { get; set; } = string.Empty;
    public string RouteName { get; set; } = string.Empty;
    public string RouteVersion { get; set; } = string.Empty;
    public bool ProductActive { get; set; }
    public bool RouteActive { get; set; }
    public string WorkOrderNumber { get; set; } = string.Empty;
    public string WorkOrderStatus { get; set; } = string.Empty;
    public DateTime? WipCreatedAt { get; set; }
    public uint? CurrentStepNumber { get; set; }
    public string? CurrentLocation { get; set; }
    public List<SearchDetailStatVm> Stats { get; set; } = new();
    public List<SearchRouteStepVm> Steps { get; set; } = new();
    public List<SearchResultVm> RelatedWorkOrders { get; set; } = new();
}

public class SearchDetailStatVm
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class SearchRouteStepVm
{
    public uint StepId { get; set; }
    public uint StepNumber { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public bool IsCurrent { get; set; }
}
