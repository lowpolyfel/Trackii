namespace Trackii.Models.Admin.HardMod;

public class HardModVm
{
    public string? Search { get; set; }
    public string? SearchMessage { get; set; }
    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public List<HardModSearchResultVm> Results { get; } = new();
}

public class HardModSearchResultVm
{
    public uint WorkOrderId { get; set; }
    public string WorkOrderNumber { get; set; } = string.Empty;
    public string ProductPartNumber { get; set; } = string.Empty;
    public int WipItemsCount { get; set; }
    public int WipStepExecutionsCount { get; set; }
    public string WipItemIdsPreview { get; set; } = string.Empty;
    public bool ExistsInWorkOrder { get; set; }
    public bool ExistsInWipItem { get; set; }
    public bool ExistsInWipStepExecution { get; set; }
}

public class HardDeleteResultVm
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
