namespace Trackii.Models.Gerencia;

public class GerenciaWoHealthVm
{
    public ChartVm StatusChart { get; } = new();
    public List<Row> Items { get; } = new();

    public class Row
    {
        public string Status { get; set; } = string.Empty;
        public int Total { get; set; }
        public int WithWip { get; set; }
    }
}
