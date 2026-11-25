using System.Text.Json.Serialization;

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
    /// <summary>Ставка НДС, %. НДС начисляется сверху на продажную цену.</summary>
    public decimal VatRate { get; set; } = 20m;
    /// <summary>Сколько единиц товара составляют одно грузовое место (для графы 10 ТТН).</summary>
    public decimal? UnitsPerPlace { get; set; }
    /// <summary>Масса одной единицы, кг (для графы 11 ТТН, масса груза в тоннах).</summary>
    public decimal? UnitMassKg { get; set; }
    [JsonIgnore] public string VatRateDisplay => VatRate.ToString("0.##") + " %";
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
    /// <summary>УНП (для покупателя печатается в ТТН).</summary>
    public string Unp { get; set; } = "";
    /// <summary>Юридический адрес.</summary>
    public string Address { get; set; } = "";
    /// <summary>Адрес разгрузки по умолчанию; если пуст, используется юридический адрес.</summary>
    public string DeliveryAddress { get; set; } = "";
    public string Phone { get; set; } = "";
    /// <summary>Договор — основание отпуска по умолчанию, например «Договор № 12 от 10.01.2026».</summary>
    public string Contract { get; set; } = "";
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
    /// <summary>Начальная настройка: склады, реквизиты организации и учётные записи — только администратор.</summary>
    public bool CanManageWarehouses => Role is UserRole.Admin;
    public bool CanManageOrganization => Role is UserRole.Admin;
    public bool CanManageUsers => Role is UserRole.Admin;
    /// <summary>Поставщиков ведёт кладовщик (приход), покупателей — менеджер продаж (реализация).</summary>
    public bool CanManageCounterparty(bool isSupplier) => Role is UserRole.Admin
        || (isSupplier ? Role is UserRole.Storekeeper : Role is UserRole.SalesManager);
    public bool CanManageAnyCounterparty => CanManageCounterparty(true) || CanManageCounterparty(false);
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

/// <summary>Строка документа (одно движение одного товара).</summary>
public sealed class OperationRow
{
    public int Id { get; init; }
    public int DocumentId { get; init; }
    public DateTime OccurredAt { get; init; }
    public OperationKind Kind { get; init; }
    public string KindName => Documents.KindName(Kind);
    public string DocumentNumber { get; init; } = "";
    public string DocumentNumberDisplay => Documents.NumberDisplay(DocumentNumber, DocumentId);
    public string ProductName { get; init; } = "";
    public decimal Quantity { get; init; }
    public string Unit { get; init; } = "";
    public decimal? Price { get; init; }
    public string FromWarehouse { get; init; } = "—";
    public string ToWarehouse { get; init; } = "—";
    public string CounterpartyName { get; init; } = "—";
    public string Comment { get; init; } = "";
    public string UserName { get; init; } = "";
    /// <summary>Знак изменения остатка в целом по организации: приход +, реализация −, перемещение 0.</summary>
    public string Direction => Kind switch { OperationKind.Receipt => "+", OperationKind.Shipment => "−", _ => "⇄" };
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
