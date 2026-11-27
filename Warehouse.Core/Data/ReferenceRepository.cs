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
        q.CommandText = "SELECT Id,Name,Unit,PurchasePrice,SalePrice,UnitWeightKg,UnitsPerCargoPlace FROM dbo.Products ORDER BY Name,Id";
        using var r = q.ExecuteReader();
        while (r.Read()) result.Add(ReadProduct(r));
        return result;
    }
    public List<WarehouseEntity> GetWarehouses()
    {
        var result = new List<WarehouseEntity>();
        using var c = Open(); using var q = c.CreateCommand();
        q.CommandText = "SELECT Id,Name,Address,ManagerName,ManagerContact FROM dbo.Warehouses ORDER BY Name,Id";
        using var r = q.ExecuteReader();
        while (r.Read()) result.Add(ReadWarehouse(r));
        return result;
    }
    public List<Counterparty> GetCounterparties()
    {
        var result = new List<Counterparty>();
        using var c = Open(); using var q = c.CreateCommand();
        q.CommandText = "SELECT Id,Name,IsSupplier,Address,Unp FROM dbo.Counterparties ORDER BY Name,Id";
        using var r = q.ExecuteReader();
        while (r.Read()) result.Add(ReadCounterparty(r));
        return result;
    }
    private static Product ReadProduct(SqlDataReader r) => new() { Id=r.GetInt32(0), Name=r.GetString(1), Unit=r.GetString(2), PurchasePrice=r.GetDecimal(3), SalePrice=r.GetDecimal(4), UnitWeightKg=r.IsDBNull(5) ? null : r.GetDecimal(5), UnitsPerCargoPlace=r.IsDBNull(6) ? null : r.GetDecimal(6) };
    private static WarehouseEntity ReadWarehouse(SqlDataReader r) => new() { Id=r.GetInt32(0), Name=r.GetString(1), Address=r.GetString(2), ManagerName=r.GetString(3), ManagerContact=r.GetString(4) };
    private static Counterparty ReadCounterparty(SqlDataReader r) => new() { Id=r.GetInt32(0), Name=r.GetString(1), IsSupplier=r.GetBoolean(2), Address=r.GetString(3), Unp=r.GetString(4) };

    private static Product? FindProduct(SqlConnection c, SqlTransaction tx, int id)
    {
        using var q=new SqlCommand("SELECT Id,Name,Unit,PurchasePrice,SalePrice,UnitWeightKg,UnitsPerCargoPlace FROM dbo.Products WITH (UPDLOCK,HOLDLOCK) WHERE Id=@id",c,tx);
        Add(q,"@id",SqlDbType.Int,id); using var r=q.ExecuteReader(); return r.Read() ? ReadProduct(r) : null;
    }
    private static WarehouseEntity? FindWarehouse(SqlConnection c, SqlTransaction tx, int id)
    {
        using var q=new SqlCommand("SELECT Id,Name,Address,ManagerName,ManagerContact FROM dbo.Warehouses WITH (UPDLOCK,HOLDLOCK) WHERE Id=@id",c,tx);
        Add(q,"@id",SqlDbType.Int,id); using var r=q.ExecuteReader(); return r.Read() ? ReadWarehouse(r) : null;
    }
    private static Counterparty? FindCounterparty(SqlConnection c, SqlTransaction tx, int id)
    {
        using var q=new SqlCommand("SELECT Id,Name,IsSupplier,Address,Unp FROM dbo.Counterparties WITH (UPDLOCK,HOLDLOCK) WHERE Id=@id",c,tx);
        Add(q,"@id",SqlDbType.Int,id); using var r=q.ExecuteReader(); return r.Read() ? ReadCounterparty(r) : null;
    }

    public void SaveProduct(Product product, AppUser actor)
    {
        var name=Validation.Required(product.Name,"Наименование",200); var unit=Validation.Required(product.Unit,"Единица измерения",30);
        Validation.Price(product.PurchasePrice,"Закупочная цена"); Validation.Price(product.SalePrice,"Продажная цена");
        if (product.UnitWeightKg is decimal weight && (weight<=0 || weight>1_000_000_000m || decimal.Round(weight,6)!=weight))
            throw new ArgumentException("Масса единицы, кг: больше нуля, не более шести знаков после запятой.");
        if (product.UnitsPerCargoPlace is decimal units && (units<=0 || units>1_000_000_000m || decimal.Round(units,3)!=units))
            throw new ArgumentException("Количество единиц на грузовое место: больше нуля, не более трёх знаков после запятой.");
        using var c=Open(); using var tx=c.BeginTransaction(IsolationLevel.Serializable);
        var actual=RequireActor(c,tx,actor,u=>u.CanManageProducts);
        var before=product.Id == 0 ? null : FindProduct(c,tx,product.Id) ?? throw new InvalidOperationException("Товар не найден.");
        int id;
        using (var q=new SqlCommand(product.Id == 0
            ? "INSERT dbo.Products(Name,Unit,PurchasePrice,SalePrice,UnitWeightKg,UnitsPerCargoPlace) OUTPUT INSERTED.Id VALUES (@name,@unit,@purchase,@sale,@weight,@unitsPerPlace)"
            : @"UPDATE dbo.Products SET Name=@name,Unit=@unit,PurchasePrice=@purchase,SalePrice=@sale,UnitWeightKg=@weight,UnitsPerCargoPlace=@unitsPerPlace OUTPUT INSERTED.Id
                WHERE Id=@id AND (Unit=@unit OR NOT EXISTS (SELECT 1 FROM dbo.StockOperations WHERE ProductId=@id))",c,tx))
        {
            Add(q,"@id",SqlDbType.Int,product.Id); Add(q,"@name",SqlDbType.NVarChar,name,200); Add(q,"@unit",SqlDbType.NVarChar,unit,30);
            Add(q,"@purchase",SqlDbType.Decimal,product.PurchasePrice); Add(q,"@sale",SqlDbType.Decimal,product.SalePrice);
            AddDecimal(q,"@weight",product.UnitWeightKg,6); Add(q,"@unitsPerPlace",SqlDbType.Decimal,product.UnitsPerCargoPlace);
            var value=q.ExecuteScalar();
            if (value is null) throw new InvalidOperationException("Товар не найден, либо единицу измерения нельзя изменить после проведения операций.");
            id=Convert.ToInt32(value);
        }
        var after=new Product { Id=id,Name=name,Unit=unit,PurchasePrice=product.PurchasePrice,SalePrice=product.SalePrice,UnitWeightKg=product.UnitWeightKg,UnitsPerCargoPlace=product.UnitsPerCargoPlace };
        WriteAudit(c,tx,actual,before is null ? "Добавлен товар" : "Изменён товар","Товар",id.ToString(),new { Before=before,After=after });
        tx.Commit(); SyncAuditFile();
    }

    public void SaveWarehouse(WarehouseEntity warehouse, AppUser actor)
    {
        var name=Validation.Required(warehouse.Name,"Наименование",200); var address=Validation.Required(warehouse.Address,"Адрес",400);
        var manager=Validation.Required(warehouse.ManagerName,"Заведующий",200); var contact=Validation.Required(warehouse.ManagerContact,"Контакты заведующего",200);
        using var c=Open(); using var tx=c.BeginTransaction(IsolationLevel.Serializable);
        var actual=RequireActor(c,tx,actor,u=>u.CanManageWarehousesAndParties);
        var before=warehouse.Id == 0 ? null : FindWarehouse(c,tx,warehouse.Id) ?? throw new InvalidOperationException("Склад не найден.");
        int id;
        using (var q=new SqlCommand(warehouse.Id == 0
            ? "INSERT dbo.Warehouses(Name,Address,ManagerName,ManagerContact) OUTPUT INSERTED.Id VALUES (@name,@address,@manager,@contact)"
            : "UPDATE dbo.Warehouses SET Name=@name,Address=@address,ManagerName=@manager,ManagerContact=@contact OUTPUT INSERTED.Id WHERE Id=@id",c,tx))
        {
            Add(q,"@id",SqlDbType.Int,warehouse.Id); Add(q,"@name",SqlDbType.NVarChar,name,200); Add(q,"@address",SqlDbType.NVarChar,address,400);
            Add(q,"@manager",SqlDbType.NVarChar,manager,200); Add(q,"@contact",SqlDbType.NVarChar,contact,200);
            var value=q.ExecuteScalar(); if (value is null) throw new InvalidOperationException("Склад не найден."); id=Convert.ToInt32(value);
        }
        var after=new WarehouseEntity { Id=id,Name=name,Address=address,ManagerName=manager,ManagerContact=contact };
        WriteAudit(c,tx,actual,before is null ? "Добавлен склад" : "Изменён склад","Склад",id.ToString(),new { Before=before,After=after });
        tx.Commit(); SyncAuditFile();
    }

    public void SaveCounterparty(Counterparty party, AppUser actor)
    {
        var name=Validation.Required(party.Name,"Наименование",200);
        var address=Validation.Optional(party.Address,"Адрес контрагента",400); var unp=Validation.Optional(party.Unp,"УНП",30);
        using var c=Open(); using var tx=c.BeginTransaction(IsolationLevel.Serializable);
        var actual=RequireActor(c,tx,actor,u=>u.CanManageWarehousesAndParties);
        var before=party.Id == 0 ? null : FindCounterparty(c,tx,party.Id) ?? throw new InvalidOperationException("Контрагент не найден.");
        int id;
        using (var q=new SqlCommand(party.Id == 0
            ? "INSERT dbo.Counterparties(Name,IsSupplier,Address,Unp) OUTPUT INSERTED.Id VALUES (@name,@supplier,@address,@unp)"
            : @"UPDATE dbo.Counterparties SET Name=@name,IsSupplier=@supplier,Address=@address,Unp=@unp OUTPUT INSERTED.Id
                WHERE Id=@id AND (IsSupplier=@supplier OR NOT EXISTS (SELECT 1 FROM dbo.StockOperations WHERE CounterpartyId=@id))",c,tx))
        {
            Add(q,"@id",SqlDbType.Int,party.Id); Add(q,"@name",SqlDbType.NVarChar,name,200); Add(q,"@supplier",SqlDbType.Bit,party.IsSupplier); Add(q,"@address",SqlDbType.NVarChar,address,400); Add(q,"@unp",SqlDbType.NVarChar,unp,30);
            var value=q.ExecuteScalar();
            if (value is null) throw new InvalidOperationException("Контрагент не найден, либо его тип нельзя изменить: он используется в операциях.");
            id=Convert.ToInt32(value);
        }
        var after=new Counterparty {Id=id,Name=name,IsSupplier=party.IsSupplier,Address=address,Unp=unp};
        WriteAudit(c,tx,actual,before is null ? "Добавлен контрагент" : "Изменён контрагент","Контрагент",id.ToString(),new { Before=before,After=after });
        tx.Commit(); SyncAuditFile();
    }

    public void DeleteProduct(int id, AppUser actor) => DeleteReference("Products",id,actor);
    public void DeleteWarehouse(int id, AppUser actor) => DeleteReference("Warehouses",id,actor);
    public void DeleteCounterparty(int id, AppUser actor) => DeleteReference("Counterparties",id,actor);
    private void DeleteReference(string table, int id, AppUser actor)
    {
        if (table is not ("Products" or "Warehouses" or "Counterparties")) throw new ArgumentException("Неизвестный справочник.");
        try
        {
            using var c=Open(); using var tx=c.BeginTransaction(IsolationLevel.Serializable);
            var actual=RequireActor(c,tx,actor,u=>table == "Products" ? u.CanManageProducts : u.CanManageWarehousesAndParties);
            object? before=table switch { "Products"=>FindProduct(c,tx,id), "Warehouses"=>FindWarehouse(c,tx,id), _=>FindCounterparty(c,tx,id) };
            if (before is null) throw new InvalidOperationException("Запись уже удалена или не найдена.");
            using (var q=new SqlCommand($"DELETE dbo.[{table}] WHERE Id=@id",c,tx))
            {
                Add(q,"@id",SqlDbType.Int,id);
                if (q.ExecuteNonQuery()!=1) throw new InvalidOperationException("Запись уже удалена или не найдена.");
            }
            var entity=table switch { "Products"=>"Товар", "Warehouses"=>"Склад", _=>"Контрагент" };
            WriteAudit(c,tx,actual,"Удалена запись справочника",entity,id.ToString(),new { Before=before });
            tx.Commit(); SyncAuditFile();
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            throw new InvalidOperationException("Удаление невозможно: запись связана с остатками или историей операций.",ex);
        }
    }
}
