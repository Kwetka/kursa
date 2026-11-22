namespace Warehouse.Core.Models;

public enum OperationKind { Receipt = 0, Shipment = 1, Transfer = 2 }
public enum UserRole { Admin = 0, Manager = 1, Storekeeper = 2, SalesManager = 3 }

public sealed class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "шт";
    public decimal PurchasePrice { get; set; }
    public decimal SalePrice { get; set; }
    public override string ToString() => Name;
}

public sealed class Warehouse
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string ManagerName { get; set; } = "";
    public string ManagerContact { get; set; } = "";
    public override string ToString() => Name;
}

public sealed class Counterparty
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsSupplier { get; set; }
    public string TypeName => IsSupplier ? "Поставщик" : "Покупатель";
    public override string ToString() => $"{Name} ({TypeName.ToLowerInvariant()})";
}

public static class UserRoles
{
    public static string Name(UserRole role) => role switch
    {
        UserRole.Admin => "Администратор",
        UserRole.Manager => "Менеджер",
        UserRole.Storekeeper => "Кладовщик",
        UserRole.SalesManager => "Менеджер продаж",
        _ => "Неизвестная роль"
    };
}

public sealed record AppUser(int Id, string Login, string FullName, UserRole Role)
{
    public bool CanManageProducts => Role is UserRole.Admin or UserRole.Storekeeper;
    public bool CanManageWarehousesAndParties => Role is UserRole.Admin;
    public bool CanManageUsers => Role is UserRole.Admin;
    public bool CanAnalyze => Role is UserRole.Admin or UserRole.Manager;
    public bool CanReceiptOrTransfer => Role is UserRole.Admin or UserRole.Storekeeper;
    public bool CanShip => Role is UserRole.Admin or UserRole.SalesManager;
    public bool CanPerform(OperationKind kind) => kind switch
    {
        OperationKind.Receipt or OperationKind.Transfer => CanReceiptOrTransfer,
        OperationKind.Shipment => CanShip,
        _ => false
    };
    public string RoleName => UserRoles.Name(Role);
}

public sealed class UserAccount
{
    public int Id { get; set; }
    public string Login { get; set; } = "";
    public string FullName { get; set; } = "";
    public UserRole Role { get; set; }
    public string RoleName => UserRoles.Name(Role);
}

public sealed record OperationInput(
    OperationKind Kind, int ProductId, decimal Quantity,
    int? FromWarehouseId, int? ToWarehouseId, int? CounterpartyId, string Comment);

public sealed class OperationRow
{
    public int Id { get; init; }
    public DateTime OccurredAt { get; init; }
    public OperationKind Kind { get; init; }
    public string KindName => Kind switch
    {
        OperationKind.Receipt => "Приход",
        OperationKind.Shipment => "Реализация",
        OperationKind.Transfer => "Перемещение",
        _ => "Неизвестно"
    };
    public string ProductName { get; init; } = "";
    public decimal Quantity { get; init; }
    public string Unit { get; init; } = "";
    public string FromWarehouse { get; init; } = "—";
    public string ToWarehouse { get; init; } = "—";
    public string CounterpartyName { get; init; } = "—";
    public string Comment { get; init; } = "";
    public string UserName { get; init; } = "";
}

public sealed class StockRow
{
    public int ProductId { get; init; }
    public int WarehouseId { get; init; }
    public string ProductName { get; init; } = "";
    public string WarehouseName { get; init; } = "";
    public string Unit { get; init; } = "";
    public decimal Quantity { get; init; }
    public decimal PurchasePrice { get; init; }
    public decimal SalePrice { get; init; }
    public DateTime? LastMovement { get; init; }
    public decimal PurchaseValue => Quantity * PurchasePrice;
    public decimal SaleValue => Quantity * SalePrice;
}

public sealed record Movement(int Id, int ProductId, DateTime OccurredAt, OperationKind Kind,
    decimal Quantity, int? FromWarehouseId, int? ToWarehouseId);

public sealed class TurnoverRow
{
    public int ProductId { get; init; }
    public string ProductName { get; init; } = "";
    public string Unit { get; init; } = "";
    public decimal AverageStock { get; init; }
    public decimal Sold { get; init; }
    public decimal? Coefficient { get; init; }
    public decimal? Days { get; init; }
    public string CoefficientDisplay => Coefficient?.ToString("N2") ?? "—";
    public string DaysDisplay => Days?.ToString("N1") ?? "—";
    public string Status => Coefficient switch
    {
        null => "Нет данных",
        0 => "Нет продаж",
        >= 3 => "Высокая",
        >= 1 => "Нормальная",
        _ => "Низкая"
    };
}
