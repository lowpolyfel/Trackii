namespace Trackii.Models.Engineering;

public class EngineeringLobbyVm
{
    public int ActiveUnregisteredCount { get; set; }
    public int TotalUnregisteredCount { get; set; }
    public int ActiveWorkOrdersCount { get; set; }
    public int OpenWorkOrdersCount { get; set; }
    public int InProgressWorkOrdersCount { get; set; }
    public int ActiveLocationsCount { get; set; }
    public List<UnregisteredPartRow> OldestUnregistered { get; } = new();
    public List<UnregisteredPartRow> RecentUnregistered { get; } = new();
    public List<ActiveWorkOrderRow> PriorityWorkOrders { get; } = new();
    public List<BreakdownRow> FamilyBreakdown { get; } = new();
    public List<BreakdownRow> LocationBreakdown { get; } = new();

    public class UnregisteredPartRow
    {
        public uint PartId { get; set; }
        public string PartNumber { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int AgeDays { get; set; }
    }

    public class ActiveWorkOrderRow
    {
        public uint WorkOrderId { get; set; }
        public string WoNumber { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Product { get; set; } = string.Empty;
        public string Family { get; set; } = string.Empty;
        public string Subfamily { get; set; } = string.Empty;
        public string Route { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public DateTime? WipCreatedAt { get; set; }
        public int? AgeDays { get; set; }
    }

    public class BreakdownRow
    {
        public string Label { get; set; } = string.Empty;
        public int Count { get; set; }
    }
}
