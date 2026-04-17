namespace Trackii.Models.Gerencia;

public class GerenciaDiscreteMapVm
{
    public string PeriodType { get; set; } = "day";
    public string? WeekValue { get; set; }
    public string? MonthValue { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string MetricView { get; set; } = "pieces";
    public string? SelectedSubfamily { get; set; }
    public string SortBy { get; set; } = "fifo";
    public DateTime SnapshotDate { get; set; }
    public string QuickRange { get; set; } = "day";
    public DiscreteInventoryMatrixVm Matrix { get; set; } = new();
    public List<string> HiddenSubfamilies { get; } = new();
    public ChartVm OrdersSummaryChart { get; } = new();
    public ChartVm TotalsComparisonChart { get; } = new();
    public int ProducedTotal { get; set; }
    public int ScrapTotal { get; set; }
    public ChartVm ProductionTrendChart { get; } = new();
    public ChartVm ScrapTrendChart { get; } = new();
    public ChartVm SubfamilyTopProductsChart { get; } = new();
    public List<SubfamilyProductStatVm> SubfamilyTopProducts { get; } = new();
}

public class DiscreteInventoryMatrixVm
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public List<string> Subfamilies { get; } = new();
    public List<DiscreteInventoryLocationRowVm> Rows { get; } = new();
    public int TotalPieces => Rows.Sum(row => row.TotalPieces);
    public int TotalOrders => Rows.Sum(row => row.TotalOrders);
}

public class DiscreteInventoryLocationRowVm
{
    public string Location { get; set; } = string.Empty;
    public List<DiscreteInventoryCellVm> Cells { get; } = new();
    public int TotalPieces { get; set; }
    public int TotalOrders { get; set; }
}

public class DiscreteInventoryCellVm
{
    public string Subfamily { get; set; } = string.Empty;
    public int Pieces { get; set; }
    public int Orders { get; set; }
}

public class SubfamilyProductStatVm
{
    public string Product { get; set; } = string.Empty;
    public int Qty { get; set; }
    public int Scrap { get; set; }
    public int Orders { get; set; }
}

public class GerenciaDayDetailVm
{
    public DateTime Day { get; set; }
    public string SortBy { get; set; } = "fifo";
    public List<DailyOrderDetailVm> Orders { get; } = new();
}

public class GerenciaDiscreteCellDetailVm
{
    public string PeriodType { get; set; } = "week";
    public string? WeekValue { get; set; }
    public string? MonthValue { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public DateTime? Day { get; set; }
    public string Location { get; set; } = string.Empty;
    public string Subfamily { get; set; } = string.Empty;
    public int TotalQty { get; set; }
    public int TotalScrap { get; set; }
    public int TotalOrders { get; set; }
    public List<DiscreteCellDailyRowVm> DailyRows { get; } = new();
    public List<DiscreteCellOrderRowVm> OrderRows { get; } = new();
}

public class DiscreteCellDailyRowVm
{
    public DateTime Day { get; set; }
    public int Qty { get; set; }
    public int Scrap { get; set; }
    public int Orders { get; set; }
}

public class DiscreteCellOrderRowVm
{
    public DateTime Day { get; set; }
    public string WoNumber { get; set; } = string.Empty;
    public string PartNumber { get; set; } = string.Empty;
    public int Qty { get; set; }
    public int Scrap { get; set; }
    public DateTime? FirstCaptureAt { get; set; }
    public DateTime? LastCaptureAt { get; set; }
    public string WoStatus { get; set; } = string.Empty;
}

public class GerenciaDiscreteDailyPanelsVm
{
    public DateTime Day { get; set; }
    public List<DailyLocationPanelVm> Locations { get; } = new();
    public int TotalOrders => Locations.Sum(item => item.OrdersCount);
    public int TotalPieces => Locations.Sum(item => item.PiecesTotal);
}

public class DailyLocationPanelVm
{
    public string Location { get; set; } = string.Empty;
    public int OrdersCount { get; set; }
    public int PiecesTotal { get; set; }
}

public class DailyOrderDetailVm
{
    public string WoNumber { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string Subfamily { get; set; } = string.Empty;
    public string? Location { get; set; }
    public DateTime? WipStartAt { get; set; }
    public int Qty { get; set; }
    public int Scrap { get; set; }
}

public class GerenciaScrapCausesVm
{
    public DateTime? Day { get; set; }
    public string? WoNumber { get; set; }
    public string? Product { get; set; }
    public List<ScrapCauseVm> Causes { get; } = new();
    public List<ScrapLogEntryVm> Entries { get; } = new();
    public int TotalQty { get; set; }
    public int TotalEvents { get; set; }
}

public class ScrapCauseVm
{
    public string Cause { get; set; } = "Sin motivo";
    public int Qty { get; set; }
    public int Events { get; set; }
}

public class ScrapLogEntryVm
{
    public DateTime CreatedAt { get; set; }
    public string WoNumber { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string ErrorCode { get; set; } = string.Empty;
    public string ErrorCategory { get; set; } = string.Empty;
    public string ErrorDescription { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public int Qty { get; set; }
    public string? Comments { get; set; }
}

public class GerenciaActiveOrdersVm
{
    public List<WorkOrderVm> ActiveOrders { get; } = new();
    public List<WorkOrderVm> InProgressOrders { get; } = new();
}

public class GerenciaDailyTrendVm
{
    public ChartVm TrendChart { get; } = new();
}

public class GerenciaErrorCausesVm
{
    public ChartVm CausesChart { get; } = new();
    public List<ScrapCauseVm> Causes { get; } = new();
    public ChartVm LocationScrapChart { get; } = new();
    public List<ScrapLocationVm> Locations { get; } = new();
    public ChartVm ErrorFrequencyChart { get; } = new();
    public ChartVm HourlyScrapChart { get; } = new();
    public List<LocationErrorMatrixRowVm> LocationErrorMatrix { get; } = new();
    public List<ProductErrorCauseVm> ProductCauseRows { get; } = new();
    public List<OrderLossVm> TopOrderLosses { get; } = new();
    public List<UserScrapActivityVm> TopReporters { get; } = new();
}

public class ScrapLocationVm
{
    public string Location { get; set; } = string.Empty;
    public int Qty { get; set; }
    public int Events { get; set; }
}

public class LocationErrorMatrixRowVm
{
    public string Location { get; set; } = string.Empty;
    public string ErrorCode { get; set; } = string.Empty;
    public int Qty { get; set; }
    public int Events { get; set; }
}

public class ProductErrorCauseVm
{
    public string Product { get; set; } = string.Empty;
    public string Cause { get; set; } = string.Empty;
    public int Qty { get; set; }
    public int Events { get; set; }
}

public class OrderLossVm
{
    public string WoNumber { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string MainLocation { get; set; } = string.Empty;
    public int Qty { get; set; }
    public int Events { get; set; }
}

public class UserScrapActivityVm
{
    public string UserName { get; set; } = string.Empty;
    public int Qty { get; set; }
    public int Events { get; set; }
    public DateTime LastRecord { get; set; }
}
