using System.Data;
using Microsoft.Data.SqlClient;
using Warehouse.Core.Models;
using WarehouseEntity = Warehouse.Core.Models.Warehouse;
using Warehouse.Core.Services;

namespace Warehouse.Core.Data;

public sealed partial class WarehouseRepository
{
    public List<Product> GetProducts()
    {
        var result = new List<Product>();
        using var c = Open(); using var q = c.CreateCommand();
        q.CommandText = "SELECT Id, Name, Unit, PurchasePrice, SalePrice FROM dbo.Products ORDER BY Name, Id";
        using var r = q.ExecuteReader();
        while (r.Read()) result.Add(new Product { Id = r.GetInt32(0), Name = r.GetString(1), Unit = r.GetString(2), PurchasePrice = r.GetDecimal(3), SalePrice = r.GetDecimal(4) });
        return result;
    }
    public List<WarehouseEntity> GetWarehouses()
    {
        var result = new List<WarehouseEntity>();
        using var c = Open(); using var q = c.CreateCommand();
        q.CommandText = "SELECT Id, Name, Address, ManagerName, ManagerContact FROM dbo.Warehouses ORDER BY Name, Id";
        using var r = q.ExecuteReader();
        while (r.Read()) result.Add(new WarehouseEntity { Id = r.GetInt32(0), Name = r.GetString(1), Address = r.GetString(2), ManagerName = r.GetString(3), ManagerContact = r.GetString(4) });
        return result;
    }
    public List<Counterparty> GetCounterparties()
    {
        var result = new List<Counterparty>();
        using var c = Open(); using var q = c.CreateCommand();
        q.CommandText = "SELECT Id, Name, IsSupplier FROM dbo.Counterparties ORDER BY Name, Id";
        using var r = q.ExecuteReader();
        while (r.Read()) result.Add(new Counterparty { Id = r.GetInt32(0), Name = r.GetString(1), IsSupplier = r.GetBoolean(2) });
        return result;
    }

    public void SaveProduct(Product product, AppUser actor)
    {
        Validation.Editor(actor);
        var name = Validation.Required(product.Name, "Наименование", 200);
        var unit = Validation.Required(product.Unit, "Единица измерения", 30);
        Validation.Price(product.PurchasePrice, "Закупочная цена"); Validation.Price(product.SalePrice, "Продажная цена");
        using var c = Open(); using var q = c.CreateCommand();
        q.CommandText = product.Id == 0
            ? "INSERT dbo.Products(Name, Unit, PurchasePrice, SalePrice) VALUES (@name,@unit,@purchase,@sale)"
            : @"UPDATE dbo.Products SET Name=@name, Unit=@unit, PurchasePrice=@purchase, SalePrice=@sale
                WHERE Id=@id AND (Unit=@unit OR NOT EXISTS (SELECT 1 FROM dbo.StockOperations WHERE ProductId=@id))";
        Add(q, "@id", SqlDbType.Int, product.Id); Add(q, "@name", SqlDbType.NVarChar, name, 200); Add(q, "@unit", SqlDbType.NVarChar, unit, 30);
        Add(q, "@purchase", SqlDbType.Decimal, product.PurchasePrice); Add(q, "@sale", SqlDbType.Decimal, product.SalePrice);
        if (q.ExecuteNonQuery() != 1) throw new InvalidOperationException("Товар не найден, либо единицу измерения нельзя изменить после проведения операций.");
    }

    public void SaveWarehouse(WarehouseEntity warehouse, AppUser actor)
    {
        Validation.Editor(actor);
        var name = Validation.Required(warehouse.Name, "Наименование", 200);
        var address = Validation.Optional(warehouse.Address, "Адрес", 400);
        var manager = Validation.Optional(warehouse.ManagerName, "Заведующий", 200);
        var contact = Validation.Optional(warehouse.ManagerContact, "Контакты заведующего", 200);
        using var c = Open(); using var q = c.CreateCommand();
        q.CommandText = warehouse.Id == 0
            ? "INSERT dbo.Warehouses(Name,Address,ManagerName,ManagerContact) VALUES (@name,@address,@manager,@contact)"
            : "UPDATE dbo.Warehouses SET Name=@name, Address=@address, ManagerName=@manager, ManagerContact=@contact WHERE Id=@id";
        Add(q, "@id", SqlDbType.Int, warehouse.Id); Add(q, "@name", SqlDbType.NVarChar, name, 200);
        Add(q, "@address", SqlDbType.NVarChar, address, 400); Add(q, "@manager", SqlDbType.NVarChar, manager, 200); Add(q, "@contact", SqlDbType.NVarChar, contact, 200);
        if (q.ExecuteNonQuery() != 1) throw new InvalidOperationException("Склад не найден или не был сохранён.");
    }

    public void SaveCounterparty(Counterparty party, AppUser actor)
    {
        Validation.Editor(actor);
        var name = Validation.Required(party.Name, "Наименование", 200);
        using var c = Open(); using var q = c.CreateCommand();
        q.CommandText = party.Id == 0
            ? "INSERT dbo.Counterparties(Name,IsSupplier) VALUES (@name,@supplier)"
            : @"UPDATE dbo.Counterparties SET Name=@name, IsSupplier=@supplier
                WHERE Id=@id AND (IsSupplier=@supplier OR NOT EXISTS (SELECT 1 FROM dbo.StockOperations WHERE CounterpartyId=@id))";
        Add(q, "@id", SqlDbType.Int, party.Id); Add(q, "@name", SqlDbType.NVarChar, name, 200); Add(q, "@supplier", SqlDbType.Bit, party.IsSupplier);
        if (q.ExecuteNonQuery() != 1) throw new InvalidOperationException("Контрагент не найден, либо его тип нельзя изменить: он используется в операциях.");
    }

    public void DeleteProduct(int id, AppUser actor) => DeleteReference("Products", id, actor);
    public void DeleteWarehouse(int id, AppUser actor) => DeleteReference("Warehouses", id, actor);
    public void DeleteCounterparty(int id, AppUser actor) => DeleteReference("Counterparties", id, actor);
    private void DeleteReference(string table, int id, AppUser actor)
    {
        Validation.Editor(actor);
        if (table is not ("Products" or "Warehouses" or "Counterparties")) throw new ArgumentException("Неизвестный справочник.");
        try
        {
            using var c = Open(); using var q = c.CreateCommand();
            q.CommandText = $"DELETE dbo.[{table}] WHERE Id=@id";
            Add(q, "@id", SqlDbType.Int, id);
            if (q.ExecuteNonQuery() != 1) throw new InvalidOperationException("Запись уже удалена или не найдена.");
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            throw new InvalidOperationException("Удаление невозможно: запись связана с остатками или историей операций.", ex);
        }
    }
}
