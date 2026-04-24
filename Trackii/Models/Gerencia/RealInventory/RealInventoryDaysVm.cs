namespace Trackii.Models.Gerencia.RealInventory;

public class RealInventoryDaysVm
{
    public List<string> DayColumns { get; } = new();
    public List<RealInventoryDayFamilyRowVm> Rows { get; } = new();
    public List<string> ValidationWarnings { get; } = new();

    public IReadOnlyDictionary<string, int> TotalsByDay =>
        DayColumns.ToDictionary(
            day => day,
            day => Rows.Sum(row => row.PiecesByDay.TryGetValue(day, out var qty) && qty.HasValue ? qty.Value : 0),
            StringComparer.OrdinalIgnoreCase);

    public int GrandTotalPieces => Rows.Sum(row => row.TotalPieces);
}

public class RealInventoryDayFamilyRowVm
{
    public string FamilyGroup { get; set; } = string.Empty;

    // int? porque null significa blanco/no aplica.
    // 0 significa que sí aplica, pero no hay piezas.
    public Dictionary<string, int?> PiecesByDay { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int TotalPieces => PiecesByDay.Values.Where(x => x.HasValue).Sum(x => x!.Value);
}
