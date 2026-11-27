using Warehouse.Core.Models;

namespace Warehouse.Core.Services;

public static class InvoiceCalculator
{
    private static decimal Money(decimal x) => decimal.Round(x, 2, MidpointRounding.AwayFromZero);

    public static InvoiceLine Product(OperationItemInput item, Product product, VatPriceMode mode)
    {
        Validation.Quantity(item.Quantity);
        if (item.PriceBeforeDiscount is null) throw new ArgumentException("Укажите цену каждой товарной позиции.");
        Validation.Price(item.PriceBeforeDiscount.Value, "Цена до скидки");
        Discount(item.DiscountPercent);
        OptionalCargo(item.CargoPlaces, item.MassTonnes);
        var note = Validation.Optional(item.Note, "Примечание к товару", 500);
        return Calculate(product.Id, false, product.Name, product.Unit, item.Quantity, item.PriceBeforeDiscount.Value,
            item.DiscountPercent, item.VatRate, item.CargoPlaces, item.MassTonnes, note, mode);
    }

    public static InvoiceLine Transport(decimal price, decimal? rate, VatPriceMode mode)
    {
        Validation.Price(price, "Стоимость транспортных услуг");
        return Calculate(null, true, "Транспортные услуги", "рейс", null, price, 0, rate, null, null, "", mode);
    }

    private static InvoiceLine Calculate(int? productId, bool service, string name, string unit, decimal? quantity,
        decimal basePrice, decimal discount, decimal? rate, int? places, decimal? mass, string note, VatPriceMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentException("Выберите порядок учёта НДС в цене.");
        VatRate(rate);
        if (mode==VatPriceMode.WithoutVat && rate.HasValue)
            throw new ArgumentException("В режиме «без НДС» ставка НДС не указывается.");
        var price = Money(basePrice * (1m - discount / 100m));
        if (price <= 0) throw new ArgumentException("Цена после скидки должна быть больше нуля.");
        var amount = Money(price * (quantity ?? 1m));
        decimal net, gross; decimal? vat;
        if (rate is null) { net = gross = amount; vat = null; }
        else if (mode == VatPriceMode.Included)
        {
            gross = amount;
            vat = Money(gross * rate.Value / (100m + rate.Value));
            net = gross - vat.Value;
        }
        else
        {
            net = amount; vat = Money(net * rate.Value / 100m); gross = net + vat.Value;
        }
        return new InvoiceLine { ProductId=productId, IsService=service, Name=name, Unit=unit, Quantity=quantity,
            PriceBeforeDiscount=basePrice, DiscountPercent=discount, UnitPrice=price, NetAmount=net,
            VatRate=rate, VatAmount=vat, GrossAmount=gross, CargoPlaces=places, MassTonnes=mass, Note=note };
    }

    public static void VatRate(decimal? rate)
    {
        if (rate is not null && (rate < 0 || rate > 100 || decimal.Round(rate.Value, 4) != rate))
            throw new ArgumentException("Ставка НДС: от 0 до 100 %, не более четырёх знаков после запятой; пусто — без НДС.");
    }
    public static void Discount(decimal discount)
    {
        if (discount < 0 || discount >= 100 || decimal.Round(discount, 2) != discount)
            throw new ArgumentException("Скидка: от 0 до менее 100 %, не более двух знаков после запятой.");
    }
    public static void OptionalCargo(int? places, decimal? mass)
    {
        if (places is < 0 or > 1_000_000_000) throw new ArgumentException("Число грузовых мест должно быть неотрицательным целым числом.");
        if (mass is not null && (mass < 0 || mass > 1_000_000_000m || decimal.Round(mass.Value, 6) != mass))
            throw new ArgumentException("Масса в тоннах: неотрицательное число, не более шести знаков после запятой.");
    }
}
