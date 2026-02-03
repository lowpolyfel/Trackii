namespace Trackii.Models.Gerencia;

public class GerenciaWorkOrdersVm
{
    public List<WorkOrderVm> ActiveWorkOrders { get; } = new();
    public List<WorkOrderVm> InProgressWorkOrders { get; } = new();
    public List<WorkOrderVm> CancelledWorkOrders { get; } = new();
    public List<DelayedWorkOrderVm> DelayedWorkOrders { get; } = new();
}
