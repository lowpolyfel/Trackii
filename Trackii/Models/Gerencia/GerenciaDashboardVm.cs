namespace Trackii.Models.Gerencia;

public class GerenciaDashboardVm
{
    public DateTime SnapshotAtUtc { get; set; }
    public int NewOrdersToday { get; set; }
    public int PreviousWeekSameDayNewOrders { get; set; }
    public decimal DayRatioPercent { get; set; }
    public decimal WeekRatioPercent { get; set; }
    public bool DayRatioUp { get; set; }
    public bool WeekRatioUp { get; set; }
    public int OnFloorTotal { get; set; }
    public int OpenOrdersCount { get; set; }
    public int FinishedOrdersCount { get; set; }
    public int CancelledOrdersCount { get; set; }
    public int HoldOrdersCount { get; set; }
    public ChartVm BacklogByLocationChart { get; } = new();
    public ChartVm ProductionByLocationChart { get; } = new();
    public ChartVm WipStatusChart { get; } = new();
    public ChartVm ScanEventChart { get; } = new();
    public ChartVm OrderStatusChart { get; } = new();
    public List<LocationProductionVm> ProductionByLocation { get; } = new();
    public List<LocationProductionVm> TopLocations { get; } = new();
    public List<LocationProductionVm> BottomLocations { get; } = new();
    public WeeklyOutputMatrixVm WeeklyOutput { get; set; } = new();
    public List<DelayedWorkOrderVm> DelayedOrders { get; } = new();
    public List<WorkOrderVm> NewOrders { get; } = new();
}
