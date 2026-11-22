using Warehouse.Core.Models;
using Warehouse.Core.Services;
using Xunit;

namespace Warehouse.Tests;

public class PermissionsAndValidationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(0.001)]
    [InlineData(1000000000.01)]
    public void PriceRejectsZeroNegativeExcessPrecisionAndAboveLimit(double price)
        => Assert.Throws<ArgumentException>(() => Validation.Price((decimal)price,"Цена"));

    [Theory]
    [InlineData(0.01)]
    [InlineData(1)]
    [InlineData(1000000000)]
    public void PriceAcceptsPositiveByAmount(double price) => Validation.Price((decimal)price,"Цена");

    [Theory]
    [InlineData(UserRole.Admin,true,true,true,true,true,true)]
    [InlineData(UserRole.Manager,false,false,false,false,false,true)]
    [InlineData(UserRole.Storekeeper,true,false,true,true,false,false)]
    [InlineData(UserRole.SalesManager,false,false,false,false,true,false)]
    public void RoleMatrix(UserRole role,bool product,bool warehouse,bool receipt,bool transfer,bool shipment,bool analytics)
    {
        var u=new AppUser(1,"u","User",role);
        Assert.Equal(product,u.CanManageProducts); Assert.Equal(warehouse,u.CanManageWarehousesAndParties);
        Assert.Equal(receipt,u.CanPerform(OperationKind.Receipt)); Assert.Equal(transfer,u.CanPerform(OperationKind.Transfer));
        Assert.Equal(shipment,u.CanPerform(OperationKind.Shipment)); Assert.Equal(analytics,u.CanAnalyze);
        Assert.Equal(role==UserRole.Admin,u.CanManageUsers);
    }

    [Fact]
    public void RequiredFieldsRejectWhitespaceAndTrimInput()
    {
        Assert.Throws<ArgumentException>(() => Validation.Required(" \t\n", "Контакт", 200));
        Assert.Equal("Минск",Validation.Required(" Минск ","Адрес",400));
    }

    [Fact]
    public void NewPasswordMustMeetLengthAndNotBeWhitespace()
    {
        Assert.Throws<ArgumentException>(() => Validation.Password("1234567"));
        Assert.Throws<ArgumentException>(() => Validation.Password("        "));
        Validation.Password("secure-password");
    }
}
