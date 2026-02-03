namespace Trackii.Models.Lobby;

public class AdminLobbyVm
{
    public int AreasCount { get; set; }
    public int FamiliesCount { get; set; }
    public int SubfamiliesCount { get; set; }
    public int ProductsCount { get; set; }
    public int RoutesCount { get; set; }
    public int LocationsCount { get; set; }
    public int UsersCount { get; set; }
    public int RolesCount { get; set; }
    public int ActiveDevicesCount { get; set; }

    public List<ActiveDeviceVm> ActiveDevices { get; } = new();
    public List<ActiveUserVm> ActiveUsers { get; } = new();

    public class ActiveDeviceVm
    {
        public string DeviceUid { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
    }

    public class ActiveUserVm
    {
        public string Username { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }
}
