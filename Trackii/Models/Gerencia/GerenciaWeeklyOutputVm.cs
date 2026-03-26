namespace Trackii.Models.Gerencia;

public class WeeklyOutputMatrixVm
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public List<string> Locations { get; } = new();
    public List<WeeklyOutputDayRowVm> Rows { get; } = new();
}

public class WeeklyOutputDayRowVm
{
    public DateTime Day { get; set; }
    public List<WeeklyOutputCellVm> Cells { get; } = new();
    public int TotalQty { get; set; }
    public int TotalScrap { get; set; }
}

public class WeeklyOutputCellVm
{
    public string Location { get; set; } = string.Empty;
    public int Qty { get; set; }
    public int Scrap { get; set; }
}

public class GerenciaWeeklyOutputVm
{
    public string PeriodType { get; set; } = "week";
    public string? WeekValue { get; set; }
    public string? MonthValue { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public WeeklyOutputMatrixVm Matrix { get; set; } = new();
}
