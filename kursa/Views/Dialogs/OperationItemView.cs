using System.Globalization;
using Warehouse.Core.Models;

namespace kursa.Views.Dialogs;
public sealed record OperationItemView(Product Product, OperationItemInput Input)
{
    private static readonly CultureInfo Culture=CultureInfo.GetCultureInfo("ru-BY");
    public string ProductName => Product.Name;
    public string Unit => Product.Unit;
    public string Quantity => Input.Quantity.ToString("0.###",Culture);
    public string Price => Input.PriceBeforeDiscount?.ToString("0.00",Culture) ?? "—";
    public string Discount => Input.PriceBeforeDiscount.HasValue ? Input.DiscountPercent.ToString("0.##",Culture)+" %" : "—";
    public string Vat => Input.PriceBeforeDiscount.HasValue ? (Input.VatRate?.ToString("0.####",Culture) ?? "без НДС") : "—";
    public string Places => Input.CargoPlaces?.ToString(Culture) ?? "—";
    public string Mass => Input.MassTonnes?.ToString("0.000###",Culture) ?? "—";
}
