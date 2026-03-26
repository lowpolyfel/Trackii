namespace Trackii.Models.Admin.HardMod;

public class HardModVm
{
    public string? WipItemLookup { get; set; }
    public string? WipItemLookupMessage { get; set; }
    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public WipItemPreviewVm? WipItemPreview { get; set; }
    public WipStepExecutionPreviewVm? WipStepExecutionPreview { get; set; }
}

public class WipItemPreviewVm
{
    public uint Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public string WorkOrderNumber { get; set; } = string.Empty;
    public string ProductPartNumber { get; set; } = string.Empty;
    public string? CurrentLocation { get; set; }
    public DateTime? CreatedAt { get; set; }
    public int StepExecutionsCount { get; set; }
    public int ScanEventsCount { get; set; }
    public int ReworkLogsCount { get; set; }
}

public class WipStepExecutionPreviewVm
{
    public uint Id { get; set; }
    public uint WipItemId { get; set; }
    public string WorkOrderNumber { get; set; } = string.Empty;
    public string ProductPartNumber { get; set; } = string.Empty;
    public string LocationName { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
    public uint QtyIn { get; set; }
    public uint QtyScrap { get; set; }
}

public class HardDeleteResultVm
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
