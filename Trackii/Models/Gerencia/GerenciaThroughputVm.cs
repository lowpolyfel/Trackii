namespace Trackii.Models.Gerencia;

public class GerenciaThroughputVm
{
    public ChartVm DailyThroughputChart { get; } = new();
    public List<Row> Items { get; } = new();

    public class Row
    {
        public DateTime Day { get; set; }
        public int QtyProduced { get; set; }
    }
}
