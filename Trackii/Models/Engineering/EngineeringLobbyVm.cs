namespace Trackii.Models.Engineering;

public class EngineeringLobbyVm
{
    public int ActiveUnregisteredCount { get; set; }
    public int TotalUnregisteredCount { get; set; }
    public List<UnregisteredPartRow> OldestUnregistered { get; } = new();
    public List<UnregisteredPartRow> RecentUnregistered { get; } = new();

    public class UnregisteredPartRow
    {
        public uint PartId { get; set; }
        public string PartNumber { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int AgeDays { get; set; }
    }
}
