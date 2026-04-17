# LobbyGerencia (DB v1) - Refactor LINQ Abril 2026

```csharp
using Microsoft.EntityFrameworkCore;
using Trackii.Models.Gerencia;

public List<GerenciaSharedVm> GetLobbyGerenciaApril2026()
{
    // PASO 1: Rango estricto de Abril 2026 (incluye inicio, excluye inicio de Mayo)
    DateTime startOfApril = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Unspecified);
    DateTime startOfMay = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Unspecified);

    // PASO 2: Catálogo base (TODOS los grupos lógicos Family + Subfamily)
    // Se materializa para construir el catálogo completo y asegurar filas con cero producción.
    var baseCatalog = _context.Subfamilies
        .AsNoTracking()
        .Include(s => s.Family)
        .Select(s => new
        {
            SubfamilyId = s.Id,
            LogicalGroupName = (s.Family.Name ?? string.Empty).Trim() + " " + (s.Name ?? string.Empty).Trim()
        })
        .ToList();

    // PASO 3: Producción estricta de Abril 2026
    // Ajustar QuantitySource si en tu modelo real la cantidad vive en otra columna/tabla.
    var aprilProduction = _context.WipStepExecutions
        .AsNoTracking()
        .Include(w => w.WorkOrder)
            .ThenInclude(wo => wo.Product)
        .Where(w => w.CreatedAt >= startOfApril && w.CreatedAt < startOfMay)
        .Select(w => new
        {
            SubfamilyId = w.WorkOrder.Product.SubfamilyId,
            WorkOrderId = (int?)w.WorkOrderId,
            QuantitySource = w.WorkOrder.Quantity
        })
        .ToList();

    // PASO 4: LEFT JOIN (GroupJoin + DefaultIfEmpty) catálogo completo vs producción de abril
    var lobbyData = baseCatalog
        .GroupJoin(
            aprilProduction,
            cat => cat.SubfamilyId,
            prod => prod.SubfamilyId,
            (cat, prodGroup) => new
            {
                GroupName = cat.LogicalGroupName,
                ProductionData = prodGroup.DefaultIfEmpty() // LEFT JOIN obligatorio
            })
        .SelectMany(
            x => x.ProductionData,
            (cat, prod) => new
            {
                GroupName = cat.GroupName,
                Quantity = prod != null ? prod.QuantitySource : 0,
                WorkOrderId = prod?.WorkOrderId
            })
        .GroupBy(x => x.GroupName)
        .Select(g => new GerenciaSharedVm
        {
            LugarNombre = g.Key,
            Piezas = g.Sum(x => x.Quantity),
            Ordenes = g.Where(x => x.WorkOrderId.HasValue)
                       .Select(x => x.WorkOrderId!.Value)
                       .Distinct()
                       .Count()
        })
        .OrderBy(x => x.LugarNombre)
        .ToList();

    return lobbyData;
}
```

> Nota: si en tu modelo `Quantity` no existe en `WorkOrder`, reemplaza `QuantitySource` por el campo correcto (por ejemplo `w.QtyIn` o equivalente).
