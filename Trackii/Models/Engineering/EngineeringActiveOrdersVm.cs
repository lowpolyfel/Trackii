namespace Trackii.Models.Engineering;

public class EngineeringActiveOrdersVm
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public uint? FamilyId { get; set; }
    public uint? SubfamilyId { get; set; }
    public uint? FocusSubfamilyId { get; set; }
    public uint? LocationId { get; set; }
    public uint? RouteId { get; set; }

    public int ActiveOrdersCount { get; set; }
    public int DistinctFamiliesCount { get; set; }
    public int DistinctSubfamiliesCount { get; set; }
    public int DistinctLocationsCount { get; set; }
    public int DistinctRoutesCount { get; set; }

    public List<FilterOption> Families { get; } = new();
    public List<SubfamilyOption> Subfamilies { get; } = new();
    public List<FilterOption> Locations { get; } = new();
    public List<FilterOption> Routes { get; } = new();
    public List<BreakdownRow> FamilySummary { get; } = new();
    public List<BreakdownRow> SubfamilySummary { get; } = new();
    public List<BreakdownRow> LocationSummary { get; } = new();
    public List<BreakdownRow> RouteSummary { get; } = new();
    public List<OrderRow> Items { get; } = new();
    public List<OrderRow> FocusSubfamilyItems { get; } = new();

    public class FilterOption
    {
        public uint Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class SubfamilyOption : FilterOption
    {
        public uint FamilyId { get; set; }
    }

    public class BreakdownRow
    {
        public string Label { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class OrderRow
    {
        public uint WorkOrderId { get; set; }
        public string WoNumber { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Product { get; set; } = string.Empty;
        public uint FamilyId { get; set; }
        public string Family { get; set; } = string.Empty;
        public uint SubfamilyId { get; set; }
        public string Subfamily { get; set; } = string.Empty;
        public uint? RouteId { get; set; }
        public string Route { get; set; } = string.Empty;
        public uint? LocationId { get; set; }
        public string Location { get; set; } = string.Empty;
        public string CurrentStep { get; set; } = string.Empty;
        public int? CurrentStepQty { get; set; }
        public string NextStep { get; set; } = string.Empty;
        public string NextLocation { get; set; } = string.Empty;
        public DateTime? WipCreatedAt { get; set; }
        public int? AgeDays { get; set; }
    }
}
