// Disposable integration test: creates and removes only a randomly named WarehouseSmoke_* database.
// WAREHOUSE_TEST_SERVER, WAREHOUSE_TEST_USER, WAREHOUSE_TEST_PASSWORD are required.
using Microsoft.Data.SqlClient;
using Warehouse.Core.Data;
using Warehouse.Core.Models;
using Warehouse.Core.Services;
using WarehouseEntity = Warehouse.Core.Models.Warehouse;

var server = Environment.GetEnvironmentVariable("WAREHOUSE_TEST_SERVER");
var login = Environment.GetEnvironmentVariable("WAREHOUSE_TEST_USER");
var password = Environment.GetEnvironmentVariable("WAREHOUSE_TEST_PASSWORD");
if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
{
    Console.Error.WriteLine("Set WAREHOUSE_TEST_SERVER, WAREHOUSE_TEST_USER and WAREHOUSE_TEST_PASSWORD. The login needs CREATE DATABASE permissions.");
    return 2;
}
var db = "WarehouseSmoke_" + Guid.NewGuid().ToString("N");
var settings = new ConnectionSettings { Server=server, Database=db, IntegratedSecurity=false, SqlLogin=login, SqlPassword=password, TrustServerCertificate=true };
var cs = settings.BuildConnectionString();
static void Check(bool condition, string description)
{
    if (!condition) throw new Exception("FAILED: " + description);
    Console.WriteLine("OK: " + description);
}
try
{
    DatabaseInitializer.Initialize(cs);
    DatabaseInitializer.Initialize(cs);
    var repo = new WarehouseRepository(cs);
    var admin = repo.Authenticate("admin", "admin") ?? throw new Exception("admin login failed");
    var store = repo.Authenticate("store", "123") ?? throw new Exception("store login failed");
    Check(repo.Authenticate("admin", "incorrect") is null, "password authentication");
    Check(!store.CanManageReferences && !store.CanAnalyze, "storekeeper permissions");
    try { repo.SaveProduct(new Product {Name="blocked",Unit="шт"}, store); throw new Exception("role not enforced"); }
    catch (UnauthorizedAccessException) { Console.WriteLine("OK: reference writes reject storekeeper"); }
    repo.SaveProduct(new Product {Name="Тест 100%",Unit="шт",PurchasePrice=10.25m,SalePrice=20.40m}, admin);
    repo.SaveProduct(new Product {Name="Другая позиция",Unit="шт",PurchasePrice=5m,SalePrice=9m}, admin);
    repo.SaveWarehouse(new WarehouseEntity {Name="Склад A",Address="Минск",ManagerName="Иванов",ManagerContact="+375"}, admin);
    repo.SaveWarehouse(new WarehouseEntity {Name="Склад B",Address="Брест",ManagerName="Петров"}, admin);
    repo.SaveCounterparty(new Counterparty {Name="Поставщик",IsSupplier=true}, admin);
    repo.SaveCounterparty(new Counterparty {Name="Покупатель",IsSupplier=false}, admin);
    var products=repo.GetProducts(); var product=products.Single(p=>p.Name=="Тест 100%");
    var warehouses=repo.GetWarehouses(); var wa=warehouses.Single(w=>w.Name=="Склад A"); var wb=warehouses.Single(w=>w.Name=="Склад B");
    var parties=repo.GetCounterparties(); var supplier=parties.Single(p=>p.IsSupplier); var buyer=parties.Single(p=>!p.IsSupplier);
    Check(wa.Address=="Минск" && wa.ManagerName=="Иванов" && wa.ManagerContact=="+375", "warehouse information persists");
    Check(product.PurchasePrice==10.25m && product.SalePrice==20.40m, "BYN prices persist");
    repo.CreateOperation(new OperationInput(OperationKind.Receipt,product.Id,10,null,wa.Id,supplier.Id,"поставка"), store);
    repo.CreateOperation(new OperationInput(OperationKind.Shipment,product.Id,3,wa.Id,null,buyer.Id,"продажа"), store);
    repo.CreateOperation(new OperationInput(OperationKind.Transfer,product.Id,2,wa.Id,wb.Id,null,"перевод"), store);
    var stocks=repo.GetStocks(new StockFilter());
    Check(stocks.Single(s=>s.WarehouseId==wa.Id).Quantity==5 && stocks.Single(s=>s.WarehouseId==wb.Id).Quantity==2, "receipt/shipment/transfer balances");
    Check(repo.GetStocks(new StockFilter { WarehouseId=wb.Id,Search="100%" }).Single().Quantity==2, "stock filtering and literal % search");
    var now=repo.GetServerTime();
    Check(repo.GetOperations(new OperationFilter { FromInclusive=now.Date,ToExclusive=now.Date.AddDays(1),Kind=OperationKind.Shipment,CounterpartyId=buyer.Id }).Count==1, "operation filters");
    var shipment=repo.GetOperations(new OperationFilter { Kind=OperationKind.Shipment }).Single();
    Check(repo.GetOperations(new OperationFilter { Kind=OperationKind.Shipment,FromInclusive=shipment.OccurredAt }).Count==1, "start timestamp is inclusive");
    Check(repo.GetOperations(new OperationFilter { Kind=OperationKind.Shipment,ToExclusive=shipment.OccurredAt }).Count==0, "end timestamp is exclusive");
    Check(repo.GetOperations(new OperationFilter { Kind=OperationKind.Shipment,WarehouseId=wb.Id }).Count==0, "unmatched warehouse excluded");
    Check(repo.GetOperations(new OperationFilter { Search="not present" }).Count==0, "search excludes other operations");
    Check(repo.GetOperations(new OperationFilter { Search="%' OR 1=1 --" }).Count==0, "search treats SQL injection as literal text");
    Check(repo.GetStocks(new StockFilter { LastMovementFromInclusive=now.Date.AddDays(1) }).Count==0, "last-movement date filter excludes rows");
    var before=repo.GetOperations(new OperationFilter()).Count;
    try { repo.CreateOperation(new OperationInput(OperationKind.Shipment,product.Id,6,wa.Id,null,buyer.Id,""), store); throw new Exception("overselling allowed"); }
    catch (InvalidOperationException) { Console.WriteLine("OK: insufficient stock rejected"); }
    Check(repo.GetOperations(new OperationFilter()).Count==before && repo.GetStocks(new StockFilter()).Single(s=>s.WarehouseId==wa.Id).Quantity==5, "failed operation rolled back");
    try { repo.CreateOperation(new OperationInput(OperationKind.Transfer,product.Id,2,wa.Id,99999999,null,"invalid target"),store); throw new Exception("missing destination allowed"); }
    catch (SqlException) { Console.WriteLine("OK: invalid destination rejected by foreign key"); }
    Check(repo.GetOperations(new OperationFilter()).Count==before && repo.GetStocks(new StockFilter()).Single(s=>s.WarehouseId==wa.Id).Quantity==5, "transfer rolls back source on destination failure");
    try { repo.SaveProduct(new Product {Id=product.Id,Name=product.Name,Unit="кг",PurchasePrice=product.PurchasePrice,SalePrice=product.SalePrice},admin); throw new Exception("unit changed despite history"); }
    catch (InvalidOperationException) { Console.WriteLine("OK: unit cannot change after movements"); }
    try { repo.DeleteProduct(product.Id,admin); throw new Exception("deleted product with history"); }
    catch (InvalidOperationException) { Console.WriteLine("OK: historical data protected by foreign keys"); }
    var movements=repo.GetMovements(new[] {product.Id}, repo.GetServerTime().AddSeconds(1));
    var manyIds=Enumerable.Range(1_000_000,501).Prepend(product.Id).ToArray();
    Check(repo.GetMovements(manyIds,repo.GetServerTime().AddSeconds(1)).Count==movements.Count, "turnover movement query handles more than 500 selected IDs");
    var turnover=TurnoverCalculator.Calculate(new[] {product}, movements, now.Date, now.AddSeconds(1));
    Check(turnover.Single().Sold==3, "turnover reads the persisted movement ledger");
    var tasks=Enumerable.Range(0,12).Select(_=>Task.Run(()=> {
        try { repo.CreateOperation(new OperationInput(OperationKind.Shipment,product.Id,1,wa.Id,null,buyer.Id,"parallel"), store); return true; }
        catch (InvalidOperationException) { return false; }
    })).ToArray();
    Task.WaitAll(tasks);
    Check(tasks.Count(t=>t.Result)==5, "parallel shipments cannot oversell");
    Check(repo.GetStocks(new StockFilter { IncludeZero=true }).Single(s=>s.WarehouseId==wa.Id).Quantity==0, "parallel operations remain consistent");
    var otherRepo = new WarehouseRepository(cs);
    Check(otherRepo.GetOperations(new OperationFilter()).Count==before+5, "data survives a new repository/connection");
    repo.ChangePassword(admin,"admin","new-password-123");
    Check(repo.Authenticate("admin","admin") is null && repo.Authenticate("admin","new-password-123") is not null, "password change persisted");
    DatabaseInitializer.Initialize(cs);
    Check(repo.Authenticate("admin","admin") is null && repo.Authenticate("admin","new-password-123") is not null, "reinitialization never restores demo passwords");
    Console.WriteLine("SQL Server integration checks passed.");
    return 0;
}
finally
{
    SqlConnection.ClearAllPools();
    var master=new SqlConnectionStringBuilder(cs) {InitialCatalog="master"}.ConnectionString;
    using var connection=new SqlConnection(master); connection.Open();
    using var command=connection.CreateCommand();
    command.CommandText=$"IF DB_ID(@db) IS NOT NULL BEGIN ALTER DATABASE [{db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{db}]; END";
    command.Parameters.AddWithValue("@db",db); command.CommandTimeout=30; command.ExecuteNonQuery();
}
