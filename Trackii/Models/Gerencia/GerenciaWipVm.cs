namespace Trackii.Models.Gerencia;

public class GerenciaWipVm
{
    public ChartVm WipStatusChart { get; } = new();
    public List<WipItemVm> WipItems { get; } = new();
}
