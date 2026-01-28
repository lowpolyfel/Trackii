namespace Trackii.Models.Admin.User;

public class UserListVm
{
    public List<Row> Items { get; set; } = new();

    public string? Search { get; set; }
    public int Page { get; set; }
    public int TotalPages { get; set; }

    public class Row
    {
        public uint Id { get; set; }
        public string Username { get; set; } = "";
        public string Role { get; set; } = "";
        public bool Active { get; set; }
    }
}
