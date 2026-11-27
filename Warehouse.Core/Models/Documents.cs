using System.Text.Json.Serialization;

namespace Warehouse.Core.Models;

/// <summary>Сохранённые администратором реквизиты своей организации.</summary>
public sealed class PartyDetails
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string Unp { get; set; } = "";
}

public enum VatPriceMode { Included = 0, Excluded = 1, WithoutVat = 2 }

/// <summary>Одна позиция движения. Данные цены и груза используются только при реализации.</summary>
public sealed record OperationItemInput(
    int ProductId, decimal Quantity, decimal? PriceBeforeDiscount = null, decimal DiscountPercent = 0,
    decimal? VatRate = null, int? CargoPlaces = null, decimal? MassTonnes = null, string Note = "");

public sealed class InvoiceDraft
{
    public string Series { get; set; } = "";
    public string Number { get; set; } = "";
    public bool UseAutomaticNumber { get; set; } = true;
    public DateTime Date { get; set; } = DateTime.Today;
    public VatPriceMode PriceMode { get; set; } = VatPriceMode.Included;
    public PartyDetails? ExpectedSender { get; set; }
    public PartyDetails Recipient { get; set; } = new();
    public int? RecipientCounterpartyId { get; set; }
    public PartyDetails Payer { get; set; } = new();
    // Менеджер продаж выбирает плательщика из карточек администратора или организацию.
    public int? PayerCounterpartyId { get; set; }
    public bool PayerIsSender { get; set; }
    public string Vehicle { get; set; } = "";
    public string Trailer { get; set; } = "";
    public string WaybillNumber { get; set; } = "";
    public string Driver { get; set; } = "";
    public string BasisName { get; set; } = "";
    public string BasisNumber { get; set; } = "";
    public DateTime? BasisDate { get; set; }
    public string LoadingPoint { get; set; } = "";
    public string UnloadingPoint { get; set; } = "";
    public decimal? TransportPrice { get; set; }
    public decimal? TransportVatRate { get; set; }
}

public sealed record BatchOperationInput(
    OperationKind Kind, int? FromWarehouseId, int? ToWarehouseId, int? CounterpartyId,
    string Comment, IReadOnlyList<OperationItemInput> Items, InvoiceDraft? Invoice = null);

/// <summary>Зафиксированные значения строки, а не ссылка на изменяемую карточку товара.</summary>
public sealed class InvoiceLine
{
    public int? ProductId { get; set; }
    public bool IsService { get; set; }
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal? Quantity { get; set; }
    public decimal PriceBeforeDiscount { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal NetAmount { get; set; }
    public decimal? VatRate { get; set; }
    public decimal? VatAmount { get; set; }
    public decimal GrossAmount { get; set; }
    public int? CargoPlaces { get; set; }
    public decimal? MassTonnes { get; set; }
    public string Note { get; set; } = "";
}

/// <summary>Неизменяемый по смыслу снимок документа; хранится в SQL в момент реализации.</summary>
public sealed class InvoiceDocument
{
    public int Id { get; set; }
    public int BatchId { get; set; }
    public string Series { get; set; } = "";
    public string Number { get; set; } = "";
    public DateTime Date { get; set; }
    public VatPriceMode PriceMode { get; set; }
    public PartyDetails Sender { get; set; } = new();
    public PartyDetails Recipient { get; set; } = new();
    public PartyDetails Payer { get; set; } = new();
    public string Vehicle { get; set; } = "";
    public string Trailer { get; set; } = "";
    public string WaybillNumber { get; set; } = "";
    public string Driver { get; set; } = "";
    public string BasisName { get; set; } = "";
    public string BasisNumber { get; set; } = "";
    public DateTime? BasisDate { get; set; }
    public string LoadingPoint { get; set; } = "";
    public string UnloadingPoint { get; set; } = "";
    public List<InvoiceLine> Lines { get; set; } = [];
    [JsonIgnore] public decimal TotalNet => Lines.Sum(x => x.NetAmount);
    [JsonIgnore] public decimal TotalVat => Lines.Sum(x => x.VatAmount ?? 0m);
    [JsonIgnore] public decimal TotalGross => Lines.Sum(x => x.GrossAmount);
    [JsonIgnore] public long? TotalCargoPlaces => Lines.Any(x => !x.IsService) && Lines.Where(x => !x.IsService).All(x => x.CargoPlaces.HasValue) ? Lines.Where(x => !x.IsService).Sum(x => (long)x.CargoPlaces!.Value) : null;
    [JsonIgnore] public decimal? TotalMassTonnes => Lines.Any(x => !x.IsService) && Lines.Where(x => !x.IsService).All(x => x.MassTonnes.HasValue) ? Lines.Where(x => !x.IsService).Sum(x => x.MassTonnes!.Value) : null;
}

public sealed record BatchCreationResult(int BatchId, InvoiceDocument? Invoice);

public sealed class OperationBatchRow
{
    public int? BatchId { get; init; }
    public int? LegacyOperationId { get; init; }
    public string DisplayId => BatchId.HasValue ? $"Г-{BatchId}" : $"О-{LegacyOperationId}";
    public DateTime OccurredAt { get; init; }
    public OperationKind Kind { get; init; }
    public string KindName => Kind switch { OperationKind.Receipt => "Приход", OperationKind.Shipment => "Реализация", OperationKind.Transfer => "Перемещение", _ => "—" };
    public int ItemCount { get; init; }
    public string FromWarehouse { get; init; } = "—";
    public string ToWarehouse { get; init; } = "—";
    public string CounterpartyName { get; init; } = "—";
    public string Comment { get; init; } = "";
    public string UserName { get; init; } = "";
    public int? InvoiceId { get; init; }
    public string InvoiceLabel { get; init; } = "—";
}
