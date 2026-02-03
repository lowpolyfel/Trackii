namespace Trackii.Models.Gerencia;

public class GerenciaProductionVm
{
    public ChartVm ProductionByLocationChart { get; } = new();
    public List<LocationProductionVm> ProductionByLocation { get; } = new();
}
