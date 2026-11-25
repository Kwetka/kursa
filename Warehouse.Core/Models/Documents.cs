using System.Text.Json.Serialization;
using Warehouse.Core.Services;

namespace Warehouse.Core.Models;

/// <summary>Реквизиты своей организации — грузоотправителя в ТТН. Заполняет администратор.</summary>
public sealed class OrganizationProfile
{
    public string Name { get; set; } = "";
    public string Unp { get; set; } = "";
    public string Address { get; set; } = "";
    public string Phone { get; set; } = "";
    /// <summary>«Отпуск разрешил»: должность и ФИО.</summary>
    public string ReleaseAllowedBy { get; set; } = "";
    /// <summary>Ставка НДС, предлагаемая для новых товаров.</summary>
    public decimal DefaultVatRate { get; set; } = 20m;
    [JsonIgnore] public bool IsFilled => !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Unp) && !string.IsNullOrWhiteSpace(Address);
}

/// <summary>Позиция нового документа.</summary>
public sealed record DocumentLineInput(int ProductId, decimal Quantity)
{
    /// <summary>Цена за единицу без НДС. Если не указана: приход и перемещение — закупочная цена, реализация — продажная со скидкой.</summary>
    public decimal? Price { get; init; }
    /// <summary>Скидка от продажной цены, % (только для реализации).</summary>
    public decimal? DiscountPercent { get; init; }
    /// <summary>Ставка НДС, %. Если не указана — ставка товара. Для перемещения НДС не начисляется.</summary>
    public decimal? VatRate { get; init; }
    /// <summary>Количество грузовых мест. Если не указано — рассчитывается по товару.</summary>
    public decimal? CargoPlaces { get; init; }
    /// <summary>Масса груза, кг. Если не указана — рассчитывается по массе единицы товара.</summary>
    public decimal? CargoMassKg { get; init; }
    /// <summary>Примечание (графа 12 ТТН).</summary>
    public string Note { get; init; } = "";
}

/// <summary>Новый складской документ: приход, реализация или перемещение с несколькими товарами.</summary>
public sealed record DocumentInput(OperationKind Kind, int? FromWarehouseId, int? ToWarehouseId, int? CounterpartyId,
    IReadOnlyList<DocumentLineInput> Lines)
{
    /// <summary>Номер ТТН (для прихода — номер накладной поставщика).</summary>
    public string Number { get; init; } = "";
    /// <summary>Дата накладной поставщика (для прихода). Для остальных документов — дата проведения.</summary>
    public DateTime? DocumentDate { get; init; }
    public string Comment { get; init; } = "";
    /// <summary>Заказчик автомобильной перевозки (плательщик).</summary>
    public string PayerName { get; init; } = "";
    public string PayerUnp { get; init; } = "";
    /// <summary>Автомобиль: марка и государственный номер.</summary>
    public string Vehicle { get; init; } = "";
    public string Trailer { get; init; } = "";
    public string Driver { get; init; } = "";
    /// <summary>Номер путевого листа.</summary>
    public string WaybillNumber { get; init; } = "";
    /// <summary>Основание отпуска (договор, счёт).</summary>
    public string Basis { get; init; } = "";
    public string LoadingPoint { get; init; } = "";
    public string UnloadingPoint { get; init; } = "";
    /// <summary>«Отпуск разрешил». Если пусто — из реквизитов организации.</summary>
    public string ReleaseAllowedBy { get; init; } = "";
    /// <summary>«Сдал грузоотправитель». Если пусто — пользователь, проводящий документ.</summary>
    public string HandedOverBy { get; init; } = "";
}

/// <summary>Строка журнала: только сам документ (операция, склады, время), без перечня товаров.</summary>
public sealed class DocumentRow
{
    public int Id { get; init; }
    public OperationKind Kind { get; init; }
    public string KindName => Documents.KindName(Kind);
    public string Number { get; init; } = "";
    public string NumberDisplay => Documents.NumberDisplay(Number, Id);
    public DateTime OccurredAt { get; init; }
    public string FromWarehouse { get; init; } = "—";
    public string ToWarehouse { get; init; } = "—";
    public string CounterpartyName { get; init; } = "—";
    public string UserName { get; init; } = "";
    public int LineCount { get; init; }
    public string Comment { get; init; } = "";
}

/// <summary>Позиция проведённого документа с расчётом стоимости и НДС (НДС сверху).</summary>
public sealed class DocumentLine
{
    public int Id { get; init; }
    public int Number { get; set; }
    public int ProductId { get; init; }
    public string ProductName { get; init; } = "";
    public string Unit { get; init; } = "";
    public decimal Quantity { get; init; }
    public decimal? Price { get; init; }
    public decimal? DiscountPercent { get; init; }
    public decimal? VatRate { get; init; }
    public decimal? CargoPlaces { get; init; }
    public decimal? CargoMassKg { get; init; }
    public string Note { get; init; } = "";
    public decimal? Amount => Price is decimal price ? Ttn.Amount(Quantity, price) : null;
    public decimal? VatAmount => Amount is decimal amount && VatRate is decimal rate ? Ttn.Vat(amount, rate) : null;
    public decimal? AmountWithVat => Amount is decimal amount ? amount + (VatAmount ?? 0m) : null;
    public decimal? CargoMassTonnes => CargoMassKg is decimal kg ? kg / 1000m : null;
    public string VatRateDisplay => VatRate is decimal rate ? rate.ToString("0.##") : "без НДС";
    /// <summary>Текст графы 12: примечание и скидка.</summary>
    public string NoteDisplay => DiscountPercent is decimal d and > 0
        ? string.Join("; ", new[] { Note, $"скидка {d:0.##} %" }.Where(x => !string.IsNullOrWhiteSpace(x)))
        : Note;
}

/// <summary>Полный документ: шапка ТТН и позиции.</summary>
public sealed class StockDocument
{
    public int Id { get; init; }
    public OperationKind Kind { get; init; }
    public string KindName => Documents.KindName(Kind);
    public string Number { get; init; } = "";
    public string NumberDisplay => Documents.NumberDisplay(Number, Id);
    public DateTime OccurredAt { get; init; }
    public DateTime? DocumentDate { get; init; }
    public DateTime EffectiveDate => (DocumentDate ?? OccurredAt).Date;
    public int? FromWarehouseId { get; init; }
    public int? ToWarehouseId { get; init; }
    public int? CounterpartyId { get; init; }
    public string FromWarehouse { get; init; } = "—";
    public string ToWarehouse { get; init; } = "—";
    public string CounterpartyName { get; init; } = "—";
    public string UserName { get; init; } = "";
    public string Comment { get; init; } = "";
    public string ShipperName { get; init; } = "";
    public string ShipperUnp { get; init; } = "";
    public string ShipperAddress { get; init; } = "";
    public string ConsigneeName { get; init; } = "";
    public string ConsigneeUnp { get; init; } = "";
    public string ConsigneeAddress { get; init; } = "";
    public string PayerName { get; init; } = "";
    public string PayerUnp { get; init; } = "";
    public string Vehicle { get; init; } = "";
    public string Trailer { get; init; } = "";
    public string Driver { get; init; } = "";
    public string WaybillNumber { get; init; } = "";
    public string Basis { get; init; } = "";
    public string LoadingPoint { get; init; } = "";
    public string UnloadingPoint { get; init; } = "";
    public string ReleaseAllowedBy { get; init; } = "";
    public string HandedOverBy { get; init; } = "";
    /// <summary>Документ создан миграцией из однотоварной операции прежней версии.</summary>
    public bool IsLegacy { get; init; }
    public List<DocumentLine> Lines { get; init; } = new();
    public TtnTotals Totals => Ttn.Totals(Lines);
}

public sealed record TtnTotals(decimal Quantity, decimal Amount, decimal VatAmount, decimal AmountWithVat, decimal CargoPlaces, decimal CargoMassTonnes);

/// <summary>Значения, которые ранее вводились в документах, — для подсказок в выпадающих списках.</summary>
public sealed class DocumentSuggestions
{
    public List<string> Vehicles { get; init; } = new();
    public List<string> Trailers { get; init; } = new();
    public List<string> Drivers { get; init; } = new();
    public List<string> Payers { get; init; } = new();
}

public static class Documents
{
    public static string KindName(OperationKind kind) => kind switch
    {
        OperationKind.Receipt => "Приход",
        OperationKind.Shipment => "Реализация",
        OperationKind.Transfer => "Перемещение",
        _ => "Неизвестно"
    };

    public static string NumberDisplay(string number, int id) => string.IsNullOrWhiteSpace(number) ? $"б/н (№ {id} в программе)" : number;
}
