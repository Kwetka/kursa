namespace Warehouse.Core.Models;

public sealed class OperationFilter
{
    public DateTime? FromInclusive { get; set; }
    public DateTime? ToExclusive { get; set; }
    public int? ProductId { get; set; }
    public int? WarehouseId { get; set; }
    public int? CounterpartyId { get; set; }
    public OperationKind? Kind { get; set; }
    /// <summary>Только позиции/документ с этим идентификатором документа.</summary>
    public int? DocumentId { get; set; }
    public string Search { get; set; } = "";
}

public sealed class StockFilter
{
    // This is the date of the last movement; quantities remain current.
    public DateTime? LastMovementFromInclusive { get; set; }
    public DateTime? LastMovementToExclusive { get; set; }
    public int? ProductId { get; set; }
    public int? WarehouseId { get; set; }
    public decimal? MinimumQuantity { get; set; }
    public bool IncludeZero { get; set; }
    public string Search { get; set; } = "";
}

public sealed record LookupItem(int? Id, string Name)
{
    public override string ToString() => Name;
}
