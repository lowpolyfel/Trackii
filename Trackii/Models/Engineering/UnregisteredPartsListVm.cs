namespace Trackii.Models.Engineering;

public class UnregisteredPartsListVm
{
    public string? Search { get; set; }
    public bool OnlyActive { get; set; } = true;
    public int Page { get; set; }
    public int TotalPages { get; set; }

    public List<Row> Items { get; } = new();

    public class Row
    {
        public uint PartId { get; set; }
        public string PartNumber { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public bool Active { get; set; }
    }
}
