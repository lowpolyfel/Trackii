namespace Trackii.Models.Admin.Device;

public class DeviceListVm
{
    public List<Row> Items { get; set; } = [];

    public int Page { get; set; }
    public int TotalPages { get; set; }

    public string? Search { get; set; }
    public bool ShowInactive { get; set; }

    public class Row
    {
        public uint Id { get; set; }
        public string DeviceUid { get; set; } = "";
        public string Name { get; set; } = "";
        public string LocationName { get; set; } = "";
        public bool Active { get; set; }
    }
}
