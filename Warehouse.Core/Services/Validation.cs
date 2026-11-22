using Warehouse.Core.Models;

namespace Warehouse.Core.Services;

public static class Validation
{
    public static string Required(string? value, string field, int maxLength)
    {
        var result = (value ?? "").Trim();
        if (result.Length == 0) throw new ArgumentException($"Заполните поле «{field}».");
        if (result.Length > maxLength) throw new ArgumentException($"Поле «{field}»: не более {maxLength} символов.");
        return result;
    }
    public static string Optional(string? value, string field, int maxLength)
    {
        var result = (value ?? "").Trim();
        if (result.Length > maxLength) throw new ArgumentException($"Поле «{field}»: не более {maxLength} символов.");
        return result;
    }
    public static void Price(decimal value, string field)
    {
        if (value < 0 || value > 1_000_000_000m || decimal.Round(value, 2) != value)
            throw new ArgumentException($"{field}: от 0 до 1 000 000 000 BYN, не более двух знаков после запятой.");
    }
    public static void Quantity(decimal value)
    {
        if (value <= 0 || value > 1_000_000_000m || decimal.Round(value, 3) != value)
            throw new ArgumentException("Количество должно быть больше нуля и не больше 1 000 000 000, не более трёх знаков после запятой.");
    }
    public static void Interval(DateTime? fromInclusive, DateTime? toExclusive)
    {
        if (fromInclusive.HasValue && toExclusive.HasValue && fromInclusive.Value >= toExclusive.Value)
            throw new ArgumentException("Начало периода должно быть раньше конца.");
    }
    public static void Editor(AppUser actor)
    {
        if (!actor.CanManageReferences) throw new UnauthorizedAccessException("У вашей роли нет доступа к справочникам.");
    }
    public static void Operation(OperationInput operation)
    {
        if (!Enum.IsDefined(operation.Kind) || operation.ProductId <= 0) throw new ArgumentException("Выберите товар и тип операции.");
        Quantity(operation.Quantity);
        switch (operation.Kind)
        {
            case OperationKind.Receipt when operation.FromWarehouseId is null && operation.ToWarehouseId > 0 && operation.CounterpartyId > 0:
            case OperationKind.Shipment when operation.FromWarehouseId > 0 && operation.ToWarehouseId is null && operation.CounterpartyId > 0:
            case OperationKind.Transfer when operation.FromWarehouseId > 0 && operation.ToWarehouseId > 0 && operation.FromWarehouseId != operation.ToWarehouseId && operation.CounterpartyId is null:
                break;
            default:
                throw new ArgumentException("Проверьте склады и контрагента. Для перемещения склады должны различаться.");
        }
        Optional(operation.Comment, "Комментарий", 500);
    }
}
