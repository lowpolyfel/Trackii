namespace Trackii.Models.Admin.ExcelGenerator;

public class ExcelGeneratorLandingVm
{
    public int RoutesBySubfamilyTotalRows { get; set; }
    public int ActiveOrdersTotalRows { get; set; }
}

public class RoutesBySubfamilyExcelVm
{
    public int TotalRows { get; set; }
    public int MaxSteps { get; set; }
    public List<string> Headers { get; set; } = new();
    public List<ExcelGeneratorRowVm> PreviewRows { get; set; } = new();
}

public class ExcelGeneratorRowVm
{
    public string Product { get; set; } = string.Empty;
    public string Family { get; set; } = string.Empty;
    public string Subfamily { get; set; } = string.Empty;
    public List<string> RouteSteps { get; set; } = new();
}

public class ActiveOrdersExcelVm
{
    public int TotalRows { get; set; }
    public List<ActiveOrderExcelRowVm> PreviewRows { get; set; } = new();
}

public class ActiveOrderExcelRowVm
{
    public string WorkOrder { get; set; } = string.Empty;
    public string PartNumber { get; set; } = string.Empty;
    public string WorkOrderStatus { get; set; } = string.Empty;
    public string WipStatus { get; set; } = string.Empty;
    public string FrozenLocation { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime LastMovementAt { get; set; }
    public int DaysStopped { get; set; }
}
