namespace Trackii.Models.Admin.Role;

public class RoleListVm
{
    public List<Row> Items { get; set; } = [];

    public class Row
    {
        public uint Id { get; set; }
        public string Name { get; set; } = "";
    }
}
