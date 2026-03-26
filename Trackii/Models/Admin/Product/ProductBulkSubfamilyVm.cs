namespace Trackii.Models.Admin.Product;

public class ProductBulkSubfamilyVm
{
    public uint? AreaId { get; set; }
    public uint? FamilyId { get; set; }
    public uint? SubfamilyId { get; set; }
    public string? Search { get; set; }
    public bool ShowInactive { get; set; }

    public uint? TargetSubfamilyId { get; set; }
    public List<uint> SelectedProductIds { get; set; } = [];

    public List<(uint Id, string Name)> Areas { get; set; } = [];
    public List<(uint Id, string Name)> Families { get; set; } = [];
    public List<(uint Id, string Name)> Subfamilies { get; set; } = [];
    public List<(uint Id, string Name, uint FamilyId)> TargetSubfamilies { get; set; } = [];

    public List<Row> Items { get; set; } = [];

    public class Row
    {
        public uint Id { get; set; }
        public string PartNumber { get; set; } = "";
        public string Area { get; set; } = "";
        public string Family { get; set; } = "";
        public string Subfamily { get; set; } = "";
        public bool Active { get; set; }
    }
}
