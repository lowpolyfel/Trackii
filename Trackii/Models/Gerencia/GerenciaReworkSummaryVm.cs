namespace Trackii.Models.Gerencia;

public class GerenciaReworkSummaryVm
{
    public ChartVm ReworkByLocationChart { get; } = new();
    public List<Row> Items { get; } = new();

    public class Row
    {
        public string Location { get; set; } = string.Empty;
        public int Qty { get; set; }
        public int Events { get; set; }
    }
}
