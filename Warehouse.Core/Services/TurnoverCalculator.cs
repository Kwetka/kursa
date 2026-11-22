using Warehouse.Core.Models;

namespace Warehouse.Core.Services;

/// <summary>
/// Average stock is time-weighted: the quantity between consecutive operations is
/// weighted by how long it remained unchanged. The end instant is exclusive.
/// Every movement before the start contributes to the opening balance.
/// </summary>
public static class TurnoverCalculator
{
    public static IReadOnlyList<TurnoverRow> Calculate(
        IEnumerable<Product> selectedProducts, IEnumerable<Movement> allMovements,
        DateTime start, DateTime endExclusive, int? warehouseId = null)
    {
        if (endExclusive <= start) throw new ArgumentException("Конец периода должен быть позже начала.");
        var products = selectedProducts.GroupBy(p => p.Id).Select(g => g.First()).OrderBy(p => p.Name).ToList();
        var ids = products.Select(p => p.Id).ToHashSet();
        var movements = allMovements.Where(m => ids.Contains(m.ProductId) && m.OccurredAt < endExclusive)
            .GroupBy(m => m.ProductId).ToDictionary(g => g.Key, g => g.OrderBy(m => m.OccurredAt).ThenBy(m => m.Id).ToList());
        var totalTicks = (decimal)(endExclusive - start).Ticks;
        var periodDays = (decimal)(endExclusive - start).Ticks / TimeSpan.TicksPerDay;
        var result = new List<TurnoverRow>();
        foreach (var product in products)
        {
            movements.TryGetValue(product.Id, out var items);
            items ??= [];
            decimal balance = 0, average = 0, sold = 0;
            var cursor = start;
            foreach (var movement in items)
            {
                var delta = Delta(movement, warehouseId);
                if (movement.OccurredAt < start)
                {
                    balance += delta;
                    continue;
                }
                average += balance * ((movement.OccurredAt - cursor).Ticks / totalTicks);
                balance += delta;
                cursor = movement.OccurredAt;
                if (movement.Kind == OperationKind.Shipment &&
                    (warehouseId is null || movement.FromWarehouseId == warehouseId))
                    sold += movement.Quantity;
            }
            average += balance * ((endExclusive - cursor).Ticks / totalTicks);
            decimal? coefficient = average > 0 ? sold / average : null;
            decimal? days = coefficient > 0 ? periodDays / coefficient.Value : null;
            result.Add(new TurnoverRow
            {
                ProductId = product.Id, ProductName = product.Name, Unit = product.Unit,
                AverageStock = average, Sold = sold, Coefficient = coefficient, Days = days
            });
        }
        return result;
    }

    private static decimal Delta(Movement m, int? warehouseId)
    {
        if (warehouseId is null)
            return (m.ToWarehouseId.HasValue ? m.Quantity : 0) - (m.FromWarehouseId.HasValue ? m.Quantity : 0);
        return (m.ToWarehouseId == warehouseId ? m.Quantity : 0) -
               (m.FromWarehouseId == warehouseId ? m.Quantity : 0);
    }
}
