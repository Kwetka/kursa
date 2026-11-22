using Warehouse.Core.Models;
using Warehouse.Core.Services;
using Xunit;

namespace Warehouse.Tests;

public class TurnoverTests
{
    private static readonly DateTime Start = new(2025, 1, 1);
    private static readonly Product A = new() { Id = 1, Name = "A", Unit = "шт" };
    private static readonly Product B = new() { Id = 2, Name = "B", Unit = "шт" };
    private static Movement Move(int id, int p, double days, OperationKind type, decimal qty, int? from, int? to)
        => new(id, p, Start.AddDays(days), type, qty, from, to);

    [Fact]
    public void AverageIsWeightedByTimeAndIncludesOpeningStock()
    {
        var events = new[] { Move(2, 1, 1, OperationKind.Shipment, 4, 1, null), Move(1, 1, -5, OperationKind.Receipt, 10, null, 1) };
        var row = Assert.Single(TurnoverCalculator.Calculate(new[] { A }, events, Start, Start.AddDays(2)));
        Assert.Equal(8m, row.AverageStock); Assert.Equal(4m, row.Sold);
        Assert.Equal(.5m, row.Coefficient); Assert.Equal(4m, row.Days);
    }

    [Fact]
    public void TransferCancelsForAllWarehousesButCountsForEachWarehouse()
    {
        var events = new[] { Move(1,1,-1,OperationKind.Receipt,10,null,1), Move(2,1,1,OperationKind.Transfer,4,1,2), Move(3,1,1.5,OperationKind.Shipment,2,2,null) };
        var all = Assert.Single(TurnoverCalculator.Calculate(new[] { A }, events, Start, Start.AddDays(2)));
        var first = Assert.Single(TurnoverCalculator.Calculate(new[] { A }, events, Start, Start.AddDays(2), 1));
        var second = Assert.Single(TurnoverCalculator.Calculate(new[] { A }, events, Start, Start.AddDays(2), 2));
        Assert.Equal(9.5m, all.AverageStock); Assert.Equal(2m, all.Sold);
        Assert.Equal(8m, first.AverageStock); Assert.Equal(0m, first.Sold);
        Assert.Equal(1.5m, second.AverageStock); Assert.Equal(2m, second.Sold);
    }

    [Fact]
    public void OnlySelectedProductsAndEventsBeforeExclusiveEndAreIncluded()
    {
        var events = new[] { Move(1,1,0,OperationKind.Receipt,10,null,1), Move(2,2,0,OperationKind.Receipt,100,null,1),
            Move(3,1,2,OperationKind.Shipment,9,1,null) };
        var row = Assert.Single(TurnoverCalculator.Calculate(new[] { A }, events, Start, Start.AddDays(2)));
        Assert.Equal(10m, row.AverageStock); Assert.Equal(0m, row.Sold); Assert.Equal(0m, row.Coefficient); Assert.Null(row.Days);
        Assert.DoesNotContain(TurnoverCalculator.Calculate(new[] { A }, events, Start, Start.AddDays(2)), x => x.ProductId == B.Id);
    }

    [Fact]
    public void ZeroAverageIsUndefinedAndNotFakeZeroOr999Days()
    {
        var row = Assert.Single(TurnoverCalculator.Calculate(new[] { B }, Array.Empty<Movement>(), Start, Start.AddDays(2)));
        Assert.Equal(0m, row.AverageStock); Assert.Null(row.Coefficient); Assert.Null(row.Days);
    }

    [Fact]
    public void InvalidPeriodIsRejected() => Assert.Throws<ArgumentException>(() => TurnoverCalculator.Calculate(new[] { A }, Array.Empty<Movement>(), Start, Start));

    [Fact]
    public void PasswordsUseRandomSaltAndAreVerified()
    {
        var one = PasswordHasher.Hash("hello world", 10_000); var two = PasswordHasher.Hash("hello world", 10_000);
        Assert.NotEqual(one.Salt, two.Salt);
        Assert.True(PasswordHasher.Verify("hello world", one.Salt, one.Hash, 10_000));
        Assert.False(PasswordHasher.Verify("wrong", one.Salt, one.Hash, 10_000));
    }

    [Fact]
    public void MovementMustHaveDifferentWarehousesAndPositiveQuantity()
    {
        Assert.Throws<ArgumentException>(() => Validation.Operation(new OperationInput(OperationKind.Transfer, 1, 2, 1, 1, null, "")));
        Assert.Throws<ArgumentException>(() => Validation.Operation(new OperationInput(OperationKind.Shipment, 1, 0, 1, null, 1, "")));
        Validation.Operation(new OperationInput(OperationKind.Transfer, 1, 2, 1, 2, null, ""));
    }
}

public class InputParserTests
{
    [Theory]
    [InlineData("12,50", 12.5)]
    [InlineData("12.50", 12.5)]
    [InlineData("1 234,56", 1234.56)]
    [InlineData("0.001", 0.001)]
    public void AcceptsCommaAndDotAsDecimals(string input, double expected)
        => Assert.Equal((decimal)expected, InputParser.Number(input,"Поле"));

    [Fact]
    public void RejectsAmbiguousSeparators() => Assert.Throws<ArgumentException>(()=>InputParser.Number("1,234.56","Поле"));

    [Fact]
    public void DayDatesIncludeTheFullEndDay()
    {
        var d=new DateTime(2025,2,1);
        var range=InputParser.Interval(d,null,d,null);
        Assert.Equal(d,range.From); Assert.Equal(d.AddDays(1),range.ToExclusive);
    }

    [Fact]
    public void EndTimeIncludesTheEntireSpecifiedMinute()
    {
        var d=new DateTime(2025,2,1);
        var range=InputParser.Interval(d,"9:30",d,"10:15");
        Assert.Equal(d.AddHours(9).AddMinutes(30),range.From);
        Assert.Equal(d.AddHours(10).AddMinutes(16),range.ToExclusive);
    }

    [Fact]
    public void MidnightBoundaryAndInvalidPeriodsAreHandled()
    {
        var d=new DateTime(2025,2,1);
        Assert.Equal(d.AddDays(1),InputParser.Interval(d,"00:00",d,"23:59").ToExclusive);
        Assert.Throws<ArgumentException>(()=>InputParser.Interval(d,"14:00",d,"13:59"));
        Assert.Throws<ArgumentException>(()=>InputParser.Interval(null,"10:00",d,null));
        Assert.Throws<ArgumentException>(()=>InputParser.Interval(d,"25:00",d,null));
    }
}
