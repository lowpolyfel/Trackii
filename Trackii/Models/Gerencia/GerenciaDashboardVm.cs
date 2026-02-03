namespace Trackii.Models.Gerencia;

public class GerenciaDashboardVm
{
    public ChartVm ProductionByLocationChart { get; } = new();
    public ChartVm WipStatusChart { get; } = new();
    public ChartVm ScanEventChart { get; } = new();
    public List<LocationProductionVm> ProductionByLocation { get; } = new();
    public List<LocationProductionVm> TopLocations { get; } = new();
    public List<LocationProductionVm> BottomLocations { get; } = new();
}
