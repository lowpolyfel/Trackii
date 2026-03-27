namespace Trackii.Models.Gerencia;

public class GerenciaDiscreteMapVm
{
    public string PeriodType { get; set; } = "week";
    public string? WeekValue { get; set; }
    public string? MonthValue { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string SortBy { get; set; } = "fifo";
    public WeeklyOutputMatrixVm Matrix { get; set; } = new();
}

public class GerenciaDayDetailVm
{
    public DateTime Day { get; set; }
    public string SortBy { get; set; } = "fifo";
    public List<DailyOrderDetailVm> Orders { get; } = new();
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
}

public class ScrapCauseVm
{
    public string Cause { get; set; } = "Sin motivo";
    public int Qty { get; set; }
    public int Events { get; set; }
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
}
