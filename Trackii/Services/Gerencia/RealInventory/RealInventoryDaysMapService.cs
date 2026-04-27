using Trackii.Models.Gerencia.RealInventory;

namespace Trackii.Services.Gerencia.RealInventory;

public class RealInventoryDaysMapService
{
    private static readonly string[] DayColumns =
    {
        "Día 1", "Día 2", "Día 3", "Día 4", "Día 5", "Día 6", "Día 7"
    };

    private static readonly Dictionary<string, Dictionary<string, string[]?>> Rules = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LATERAL LED"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Día 1"] = null,
            ["Día 2"] = null,
            ["Día 3"] = new[] { "Alloy" },
            ["Día 4"] = null,
            ["Día 5"] = null,
            ["Día 6"] = new[] { "Moldeo", "Backfill", "FAST CAST", "Inspeccion Final", "Tie bar" },
            ["Día 7"] = new[] { "Tin plate", "Prueba Electrica", "Empaque", "QC" }
        },
        ["LATERAL SENSOR"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Día 1"] = null,
            ["Día 2"] = null,
            ["Día 3"] = null,
            ["Día 4"] = new[] { "Alloy" },
            ["Día 5"] = null,
            ["Día 6"] = new[] { "Moldeo", "Backfill", "FAST CAST", "Inspeccion Final", "Tie bar" },
            ["Día 7"] = new[] { "Tin plate", "Prueba Electrica", "Empaque", "QC" }
        },
        ["LATERAL OPB"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Día 1"] = null,
            ["Día 2"] = new[] { "Alloy" },
            ["Día 3"] = null,
            ["Día 4"] = null,
            ["Día 5"] = new[] { "Moldeo", "Backfill", "FAST CAST", "Inspeccion Final", "Tie bar" },
            ["Día 6"] = new[] { "Tin plate" },
            ["Día 7"] = new[] { "Prueba Electrica", "Empaque", "QC" }
        },
        ["MINI AXIAL"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Día 1"] = null,
            ["Día 2"] = null,
            ["Día 3"] = new[] { "Alloy" },
            ["Día 4"] = new[] { "Moldeo", "Backfill" },
            ["Día 5"] = new[] { "Inspeccion Final", "Tie bar", "Tin plate" },
            ["Día 6"] = null,
            ["Día 7"] = new[] { "Prueba Electrica", "Empaque", "QC" }
        },
        ["MINI AXIAL OPB"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Día 1"] = new[] { "Alloy" },
            ["Día 2"] = new[] { "Backfill" },
            ["Día 3"] = new[] { "Inspeccion Final", "Tie bar", "Tin plate" },
            ["Día 4"] = null,
            ["Día 5"] = null,
            ["Día 6"] = null,
            ["Día 7"] = new[] { "Prueba Electrica", "Empaque", "QC" }
        },
        ["MAXI AXIAL"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Día 1"] = null,
            ["Día 2"] = new[] { "Alloy" },
            ["Día 3"] = null,
            ["Día 4"] = new[] { "Moldeo", "Backfill" },
            ["Día 5"] = new[] { "FAST CAST", "Inspeccion Final" },
            ["Día 6"] = new[] { "Tie bar", "Tin plate" },
            ["Día 7"] = new[] { "Prueba Electrica", "Empaque", "QC" }
        },
        ["FOTOLOGICO"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Día 1"] = null,
            ["Día 2"] = null,
            ["Día 3"] = new[] { "Alloy" },
            ["Día 4"] = new[] { "Moldeo", "Backfill" },
            ["Día 5"] = null,
            ["Día 6"] = new[] { "FAST CAST", "Inspeccion Final", "Tie bar", "Tin plate" },
            ["Día 7"] = new[] { "Prueba Electrica", "Empaque", "QC" }
        },
        ["PHOTO OPBS"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Día 1"] = null,
            ["Día 2"] = null,
            ["Día 3"] = null,
            ["Día 4"] = new[] { "Alloy" },
            ["Día 5"] = new[] { "Moldeo", "Backfill" },
            ["Día 6"] = new[] { "Inspeccion Final", "Tie bar", "Tin plate" },
            ["Día 7"] = new[] { "Prueba Electrica", "Empaque", "QC" }
        }
    };

    public RealInventoryDaysVm BuildDaysMap(RealInventoryMapVm sourceMap)
    {
        var vm = new RealInventoryDaysVm();
        vm.DayColumns.AddRange(DayColumns);

        foreach (var familyGroup in sourceMap.Columns)
        {
            var row = new RealInventoryDayFamilyRowVm { FamilyGroup = familyGroup };
            Rules.TryGetValue(familyGroup, out var dayRule);

            foreach (var day in DayColumns)
            {
                if (dayRule is null || !dayRule.TryGetValue(day, out var locations) || locations is null)
                {
                    row.PiecesByDay[day] = null;
                    row.LocationsByDay[day] = null;
                    continue;
                }

                var qty = sourceMap.Rows
                    .Where(r => locations.Contains(r.LocationName, StringComparer.OrdinalIgnoreCase))
                    .Sum(r => r.PiecesByColumn.TryGetValue(familyGroup, out var value) ? value : 0);
                row.PiecesByDay[day] = qty;
                row.LocationsByDay[day] = locations.ToList();
            }

            vm.Rows.Add(row);

            var sourceTotalExcludingAlmacen = sourceMap.Rows
                .Where(r => !r.LocationName.Equals("Almacen", StringComparison.OrdinalIgnoreCase))
                .Sum(r => r.PiecesByColumn.TryGetValue(familyGroup, out var value) ? value : 0);

            if (row.TotalPieces != sourceTotalExcludingAlmacen)
            {
                vm.ValidationWarnings.Add(
                    $"Descuadre en {familyGroup}: días={row.TotalPieces:N0} vs mapa(base sin Almacen)={sourceTotalExcludingAlmacen:N0}.");
            }
        }

        return vm;
    }
}
