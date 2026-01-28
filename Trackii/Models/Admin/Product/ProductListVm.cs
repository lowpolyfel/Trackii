namespace Trackii.Models.Admin.Product;

public class ProductListVm
{
    public List<Row> Items { get; set; } = [];

    public int Page { get; set; }
    public int TotalPages { get; set; }

    public uint? AreaId { get; set; }
    public uint? FamilyId { get; set; }
    public uint? SubfamilyId { get; set; }

    public string? Search { get; set; }
    public bool ShowInactive { get; set; }

    public List<(uint Id, string Name)> Areas { get; set; } = [];
    public List<(uint Id, string Name)> Families { get; set; } = [];
    public List<(uint Id, string Name)> Subfamilies { get; set; } = [];

    public class Row
    {
        public uint Id { get; set; }
        public string PartNumber { get; set; } = "";
        public string Subfamily { get; set; } = "";
        public string Family { get; set; } = "";
        public string Area { get; set; } = "";
        public bool Active { get; set; }
    }
}
