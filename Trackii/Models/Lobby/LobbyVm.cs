namespace Trackii.Models.Lobby;

public class LobbyVm
{
    public int AreasCount { get; set; }
    public int FamiliesCount { get; set; }
    public int SubfamiliesCount { get; set; }
    public int ProductsCount { get; set; }
    public int RoutesCount { get; set; }
    public int LocationsCount { get; set; }
    public int UsersCount { get; set; }
    public int RolesCount { get; set; }

    public ChartVm AreaProductChart { get; set; } = new();
    public ChartVm ProductStatusChart { get; set; } = new();
    public ChartVm WorkOrderStatusChart { get; set; } = new();
    public ChartVm WipStatusChart { get; set; } = new();
    public ChartVm UsersByRoleChart { get; set; } = new();

    public class ChartVm
    {
        public List<string> Labels { get; set; } = new();
        public List<int> Values { get; set; } = new();
    }
}
