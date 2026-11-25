using Warehouse.Core.Models;
using Warehouse.Core.Services;
using Xunit;

namespace Warehouse.Tests;

public class DocumentsAndTtnTests
{
    private static DocumentInput Shipment(params DocumentLineInput[] lines) => new(OperationKind.Shipment, 1, null, 2, lines);

    [Fact]
    public void VatIsChargedOnTopOfPriceWithoutVat()
    {
        // 3 × 12,35 = 37,05; НДС 20 % = 7,41; с НДС = 44,46.
        var line = new DocumentLine { Quantity = 3, Price = 12.35m, VatRate = 20 };
        Assert.Equal(37.05m, line.Amount);
        Assert.Equal(7.41m, line.VatAmount);
        Assert.Equal(44.46m, line.AmountWithVat);
    }

    [Fact]
    public void AmountsAreRoundedToKopecksAwayFromZero()
    {
        Assert.Equal(0.13m, Ttn.Amount(0.25m, 0.5m));          // 0,125 → 0,13
        Assert.Equal(0.03m, Ttn.Vat(0.125m, 20));               // 0,025 → 0,03
        Assert.Equal(7.83m, Ttn.Discounted(7.99m, 2));          // скидка 2 % от 7,99
        Assert.Equal(7.99m, Ttn.Discounted(7.99m, null));
    }

    [Fact]
    public void TransferLinesHaveNoVatAndTotalsSumAllColumns()
    {
        var lines = new List<DocumentLine>
        {
            new() { Quantity = 2, Price = 10m, VatRate = 20, CargoPlaces = 1, CargoMassKg = 500 },
            new() { Quantity = 1.5m, Price = 4m, VatRate = 10, CargoPlaces = 2, CargoMassKg = 250 },
            new() { Quantity = 1, Price = 3m, VatRate = null }
        };
        var t = Ttn.Totals(lines);
        Assert.Equal(4.5m, t.Quantity);
        Assert.Equal(29m, t.Amount);
        Assert.Equal(4.6m, t.VatAmount);
        Assert.Equal(33.6m, t.AmountWithVat);
        Assert.Equal(3m, t.CargoPlaces);
        Assert.Equal(0.75m, t.CargoMassTonnes);
        Assert.Equal("без НДС", lines[2].VatRateDisplay);
        Assert.Null(lines[2].VatAmount);
        Assert.Equal(3m, lines[2].AmountWithVat);
    }

    [Fact]
    public void CargoPlacesRoundUpAndMassUsesUnitMass()
    {
        Assert.Equal(3m, Ttn.Places(25, 12));
        Assert.Null(Ttn.Places(25, null));
        Assert.Equal(12.5m, Ttn.MassKg(25, 0.5m));
    }

    [Theory]
    [InlineData(0, "Ноль белорусских рублей 00 копеек")]
    [InlineData(1.01, "Один белорусский рубль 01 копейка")]
    [InlineData(22.5, "Двадцать два белорусских рубля 50 копеек")]
    [InlineData(1234.12, "Одна тысяча двести тридцать четыре белорусских рубля 12 копеек")]
    [InlineData(2000011, "Два миллиона одиннадцать белорусских рублей 00 копеек")]
    public void AmountInWords(double amount, string expected) => Assert.Equal(expected, Ttn.AmountInWords((decimal)amount));

    [Fact]
    public void DiscountIsShownInNote()
    {
        var line = new DocumentLine { Quantity = 1, Price = 7.83m, DiscountPercent = 2, Note = "розн. цена = 7,99" };
        Assert.Equal("розн. цена = 7,99; скидка 2 %", line.NoteDisplay);
    }

    [Theory]
    [InlineData("")]
    [InlineData("190000001")]
    [InlineData(" 1900 00001 ")]
    [InlineData("ab1234567")]
    public void UnpAcceptsNineCharactersOrEmpty(string unp) => Assert.True(Validation.Unp(unp, "УНП").Length is 0 or 9);

    [Theory]
    [InlineData("12345678")]
    [InlineData("1234567890")]
    [InlineData("12345678-")]
    public void UnpRejectsWrongFormat(string unp) => Assert.Throws<ArgumentException>(() => Validation.Unp(unp, "УНП"));

    [Fact]
    public void DocumentNeedsLinesAndUniqueProducts()
    {
        Assert.Throws<ArgumentException>(() => Validation.Document(Shipment()));
        Assert.Throws<ArgumentException>(() => Validation.Document(Shipment(new DocumentLineInput(5, 1), new DocumentLineInput(5, 2))));
        Assert.Throws<ArgumentException>(() => Validation.Document(Shipment(new DocumentLineInput(5, 0))));
        Assert.Throws<ArgumentException>(() => Validation.Document(Shipment(new DocumentLineInput(5, 1) { Price = 0 })));
        Assert.Throws<ArgumentException>(() => Validation.Document(Shipment(new DocumentLineInput(5, 1) { DiscountPercent = 100 })));
        Assert.Throws<ArgumentException>(() => Validation.Document(Shipment(new DocumentLineInput(5, 1) { VatRate = 101 })));
    }

    [Fact]
    public void DocumentRoutingMatchesKind()
    {
        var line = new[] { new DocumentLineInput(1, 1) };
        Assert.Throws<ArgumentException>(() => Validation.Document(new DocumentInput(OperationKind.Transfer, 1, 1, null, line)));
        Assert.Throws<ArgumentException>(() => Validation.Document(new DocumentInput(OperationKind.Receipt, 1, 2, 3, line)));
        Assert.Throws<ArgumentException>(() => Validation.Document(new DocumentInput(OperationKind.Shipment, 1, null, null, line)));
        Validation.Document(new DocumentInput(OperationKind.Transfer, 1, 2, null, line));
        Validation.Document(new DocumentInput(OperationKind.Receipt, null, 2, 3, line));
    }

    [Fact]
    public void DocumentNormalizationTrimsTextAndDropsVatAndDiscountWhereNotApplicable()
    {
        var transfer = Validation.Document(new DocumentInput(OperationKind.Transfer, 1, 2, null,
            new[] { new DocumentLineInput(1, 1) { VatRate = 20, Note = "  хрупкое " } }) { Vehicle = "  МАЗ 1234 АВ-7 ", PayerUnp = " 190000001 " });
        Assert.Null(transfer.Lines[0].VatRate);
        Assert.Equal("хрупкое", transfer.Lines[0].Note);
        Assert.Equal("МАЗ 1234 АВ-7", transfer.Vehicle);
        Assert.Equal("190000001", transfer.PayerUnp);
        Assert.Throws<ArgumentException>(() => Validation.Document(new DocumentInput(OperationKind.Receipt, null, 2, 3,
            new[] { new DocumentLineInput(1, 1) { DiscountPercent = 5 } })));
        var shipment = Validation.Document(Shipment(new DocumentLineInput(1, 1) { DiscountPercent = 0 }) with { DocumentDate = new DateTime(2026, 1, 1) });
        Assert.Null(shipment.Lines[0].DiscountPercent);
        Assert.Null(shipment.DocumentDate);
    }

    [Fact]
    public void OrganizationRequiresNameUnpAndAddress()
    {
        Assert.Throws<ArgumentException>(() => Validation.Organization(new OrganizationProfile { Name = "ООО", Address = "Минск" }));
        var profile = Validation.Organization(new OrganizationProfile { Name = " ООО «Склад» ", Unp = "190000001", Address = "Минск", DefaultVatRate = 20 });
        Assert.Equal("ООО «Склад»", profile.Name);
        Assert.True(profile.IsFilled);
    }
}
