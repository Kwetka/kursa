using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Warehouse.Core.Models;
using Warehouse.Core.Services;

namespace kursa.Infrastructure;

/// <summary>
/// Редактируемая строка многотоварного документа. Значения хранятся текстом, чтобы ввод с запятой
/// или точкой разбирался так же, как в остальных окнах; расчёт стоимости и НДС (сверху) обновляется сразу.
/// </summary>
public sealed class DocumentLineEditor : INotifyPropertyChanged
{
    private readonly OperationKind _kind;
    private string _quantity = "1", _price = "", _discount = "", _vat = "", _places = "", _mass = "", _note = "";
    private bool _placesManual, _massManual;
    private int _number;
    private decimal? _available;

    public DocumentLineEditor(Product product, OperationKind kind, decimal quantity)
    {
        Product = product; _kind = kind;
        _quantity = Format(quantity);
        _price = Format(kind == OperationKind.Shipment ? product.SalePrice : product.PurchasePrice, "0.00");
        _vat = kind == OperationKind.Transfer ? "" : Format(product.VatRate, "0.##");
        RecalculateCargo();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public Product Product { get; }
    public string ProductName => Product.Name;
    public string Unit => Product.Unit;
    public int Number { get => _number; set { _number = value; Notify(); } }
    public decimal? Available { get => _available; set { _available = value; Notify(); Notify(nameof(AvailableDisplay)); Notify(nameof(IsShort)); } }
    public string AvailableDisplay => _available is decimal a ? Format(a) : "0";
    /// <summary>Количество больше остатка на складе-отправителе.</summary>
    public bool IsShort => _kind != OperationKind.Receipt && Quantity is decimal q && q > (_available ?? 0m);

    public string QuantityText
    {
        get => _quantity;
        set { _quantity = value; RecalculateCargo(); NotifyCalculated(); Notify(nameof(IsShort)); }
    }
    public string PriceText { get => _price; set { _price = value; NotifyCalculated(); } }
    /// <summary>Скидка от продажной цены товара; при изменении пересчитывается цена.</summary>
    public string DiscountText
    {
        get => _discount;
        set
        {
            _discount = value;
            if (TryParse(value, out var d) && d is >= 0 and < 100) _price = Format(Ttn.Discounted(Product.SalePrice, d), "0.00");
            Notify(); Notify(nameof(PriceText)); NotifyCalculated();
        }
    }
    public string VatText { get => _vat; set { _vat = value; NotifyCalculated(); } }
    public string PlacesText { get => _places; set { _places = value; _placesManual = !string.IsNullOrWhiteSpace(value); NotifyCalculated(); } }
    public string MassText { get => _mass; set { _mass = value; _massManual = !string.IsNullOrWhiteSpace(value); NotifyCalculated(); } }
    public string Note { get => _note; set { _note = value; Notify(); } }

    public decimal? Quantity => TryParse(_quantity, out var q) ? q : null;
    public decimal? Price => TryParse(_price, out var p) ? p : null;
    public decimal? VatRate => _kind == OperationKind.Transfer ? null : TryParse(_vat, out var v) ? v : null;
    public decimal? Amount => Quantity is decimal q && Price is decimal p ? Ttn.Amount(q, p) : null;
    public decimal? VatAmount => Amount is decimal a && VatRate is decimal r ? Ttn.Vat(a, r) : null;
    public decimal? AmountWithVat => Amount is decimal a ? a + (VatAmount ?? 0m) : null;
    public decimal? Places => TryParse(_places, out var x) ? x : null;
    public decimal? MassKg => TryParse(_mass, out var x) ? x : null;
    public string AmountDisplay => Amount?.ToString("N2", CultureInfo.CurrentCulture) ?? "—";
    public string VatAmountDisplay => VatAmount?.ToString("N2", CultureInfo.CurrentCulture) ?? "—";
    public string AmountWithVatDisplay => AmountWithVat?.ToString("N2", CultureInfo.CurrentCulture) ?? "—";

    public void AddQuantity(decimal quantity) => QuantityText = Format((Quantity ?? 0m) + quantity);

    /// <summary>Строгий разбор при проведении: ошибки сообщаются с номером позиции.</summary>
    public DocumentLineInput ToInput()
    {
        string field(string name) => $"Позиция {Number} ({Product.Name}), {name}";
        return new DocumentLineInput(Product.Id, InputParser.Number(_quantity, field("количество")))
        {
            Price = InputParser.OptionalNumber(_price, field("цена")),
            DiscountPercent = _kind == OperationKind.Shipment ? InputParser.OptionalNumber(_discount, field("скидка")) : null,
            VatRate = _kind == OperationKind.Transfer ? null : InputParser.OptionalNumber(_vat, field("ставка НДС")),
            CargoPlaces = InputParser.OptionalNumber(_places, field("грузовые места")),
            CargoMassKg = InputParser.OptionalNumber(_mass, field("масса")),
            Note = _note
        };
    }

    private void RecalculateCargo()
    {
        if (Quantity is not decimal q) return;
        if (!_placesManual) { _places = Ttn.Places(q, Product.UnitsPerPlace) is decimal p ? Format(p) : ""; Notify(nameof(PlacesText)); }
        if (!_massManual) { _mass = Ttn.MassKg(q, Product.UnitMassKg) is decimal m ? Format(m) : ""; Notify(nameof(MassText)); }
    }

    private void NotifyCalculated()
    {
        foreach (var name in new[] { nameof(QuantityText), nameof(PriceText), nameof(VatText), nameof(PlacesText), nameof(MassText),
                     nameof(AmountDisplay), nameof(VatAmountDisplay), nameof(AmountWithVatDisplay) })
            Notify(name);
    }

    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static bool TryParse(string text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        try { value = InputParser.Number(text, ""); return true; } catch (ArgumentException) { return false; }
    }

    private static string Format(decimal value, string format = "0.###") => value.ToString(format, CultureInfo.CurrentCulture);
}
