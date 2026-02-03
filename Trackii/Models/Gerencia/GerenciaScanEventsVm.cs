namespace Trackii.Models.Gerencia;

public class GerenciaScanEventsVm
{
    public ChartVm ScanEventChart { get; } = new();
    public List<ScanEventVm> RecentEvents { get; } = new();
}
