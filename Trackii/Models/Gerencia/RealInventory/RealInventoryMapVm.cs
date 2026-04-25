namespace Trackii.Models.Gerencia.RealInventory;

public class RealInventoryMapVm
{
    public DateTime SnapshotAtUtc { get; set; }
    public DateTime DataCutoffUtc { get; set; }
    public List<string> Columns { get; } = new();
    public List<RealInventoryLocationRowVm> Rows { get; } = new();
    public int GrandTotalPieces => Rows.Sum(x => x.TotalPieces);
    public IReadOnlyDictionary<string, int> TotalsByColumn =>
        Columns.ToDictionary(
            column => column,
            column => Rows.Sum(row => row.PiecesByColumn.TryGetValue(column, out var qty) ? qty : 0),
            StringComparer.OrdinalIgnoreCase);
}

public class RealInventoryLocationRowVm
{
    public string LocationName { get; set; } = string.Empty;
    public Dictionary<string, int> PiecesByColumn { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int TotalPieces => PiecesByColumn.Values.Sum();
}

public class RealInventoryCellDetailVm
{
    public string Location { get; set; } = string.Empty;
    public string FamilyGroup { get; set; } = string.Empty;
    public List<RealInventoryOrderRowVm> Orders { get; } = new();
    public int TotalQty => Orders.Sum(x => x.Qty);
}

public class RealInventoryOrderRowVm
{
    public string WoNumber { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string Family { get; set; } = string.Empty;
    public string Subfamily { get; set; } = string.Empty;
    public string SourceLocation { get; set; } = string.Empty;
    public string CurrentLocation { get; set; } = string.Empty;
    public int Qty { get; set; }
    public string WoStatus { get; set; } = string.Empty;
    public string WipStatus { get; set; } = string.Empty;
    public DateTime? LastMovementAt { get; set; }
}
