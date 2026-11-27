// Disposable integration test: creates/removes ONLY randomly named WarehouseSmoke_* databases.
// WAREHOUSE_TEST_SERVER, WAREHOUSE_TEST_USER, WAREHOUSE_TEST_PASSWORD are required.
using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;
using Warehouse.Core.Data;
using Warehouse.Core.Models;
using Warehouse.Core.Services;
using WarehouseEntity = Warehouse.Core.Models.Warehouse;

var server=Environment.GetEnvironmentVariable("WAREHOUSE_TEST_SERVER");
var login=Environment.GetEnvironmentVariable("WAREHOUSE_TEST_USER");
var password=Environment.GetEnvironmentVariable("WAREHOUSE_TEST_PASSWORD");
if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
{
    Console.Error.WriteLine("Set WAREHOUSE_TEST_SERVER, WAREHOUSE_TEST_USER and WAREHOUSE_TEST_PASSWORD. The login needs CREATE DATABASE permissions.");
    return 2;
}
var suffix=Guid.NewGuid().ToString("N");
var db="WarehouseSmoke_"+suffix;
var migrationDb="WarehouseSmoke_Migration_"+suffix;
var migrationV2Db="WarehouseSmoke_MigrationV2_"+suffix;
var auditDir=Path.Combine(Path.GetTempPath(),"WarehouseSmoke_"+suffix);
var settings=new ConnectionSettings {Server=server,Database=db,IntegratedSecurity=false,SqlLogin=login,SqlPassword=password,TrustServerCertificate=true};
// Synchronous parallel clients need spare pool threads for SqlClient's async handshake on small CI machines.
ThreadPool.GetMinThreads(out var workerThreads,out var ioThreads);
ThreadPool.SetMinThreads(Math.Max(workerThreads,24),Math.Max(ioThreads,24));
var cs=new SqlConnectionStringBuilder(settings.BuildConnectionString()) {ConnectTimeout=30}.ConnectionString;
var migrationCs=new SqlConnectionStringBuilder(cs) {InitialCatalog=migrationDb}.ConnectionString;
var migrationV2Cs=new SqlConnectionStringBuilder(cs) {InitialCatalog=migrationV2Db}.ConnectionString;
int checks=0;
void Check(bool condition,string description)
{
    if (!condition) throw new Exception("FAILED: "+description);
    checks++; Console.WriteLine("OK: "+description);
}
void Denied(Action action,string description)
{
    try { action(); throw new Exception("FAILED (not denied): "+description); }
    catch (UnauthorizedAccessException) { checks++; Console.WriteLine("OK: "+description); }
}
void Invalid(Action action,string description)
{
    try { action(); throw new Exception("FAILED (not rejected): "+description); }
    catch (ArgumentException) { checks++; Console.WriteLine("OK: "+description); }
}
void SqlConstraint(string connectionString,string statement,string description)
{
    try { using var c=new SqlConnection(connectionString); c.Open(); using var q=new SqlCommand(statement,c); q.ExecuteNonQuery(); throw new Exception("FAILED (SQL accepted): "+description); }
    catch (SqlException ex) when (ex.Number==547) { checks++; Console.WriteLine("OK: "+description); }
}
try
{
    DatabaseInitializer.Initialize(cs);
    DatabaseInitializer.Initialize(cs);
    var repo=new WarehouseRepository(cs,auditDir,Guid.NewGuid());
    var admin=repo.Authenticate("admin","admin") ?? throw new Exception("admin login failed");
    var manager=repo.Authenticate("manager","123") ?? throw new Exception("manager login failed");
    var store=repo.Authenticate("store","123") ?? throw new Exception("store login failed");
    Check(repo.Authenticate("admin","incorrect") is null,"password authentication and unsuccessful login");
    Check(repo.AuditStatus.State==AuditFileState.Verified && File.Exists(repo.AuditFilePath),"logins recorded and file matches SQL");
    Check(repo.GetAuditEntries().Count(x=>x.Action=="Вход в программу")==3 && repo.GetAuditEntries().Count(x=>x.Action=="Неуспешный вход")==1,"successful and unsuccessful logins audited");

    repo.SaveUser(new UserAccount {Login="sales",FullName="Менеджер продаж",Role=UserRole.SalesManager},"SalesPassword-99",admin);
    var sales=repo.Authenticate("sales","SalesPassword-99") ?? throw new Exception("sales login failed");
    var salesAccount=repo.GetUsers(admin).Single(x=>x.Login=="sales");
    repo.SaveUser(new UserAccount {Id=salesAccount.Id,Login="sales2",FullName="Сотрудник продаж",Role=UserRole.SalesManager},"NewSalesPassword-88",admin);
    Check(repo.Authenticate("sales","SalesPassword-99") is null && repo.Authenticate("sales2","NewSalesPassword-88") is not null,"admin can create/edit account, login and reset password");
    sales=repo.Authenticate("sales2","NewSalesPassword-88")!;
    Denied(()=>repo.GetUsers(manager),"manager cannot manage accounts");
    Denied(()=>repo.SaveUser(new UserAccount {Login="bad",FullName="Bad",Role=UserRole.Admin},"ValidPass-99",store),"storekeeper cannot create accounts");
    Invalid(()=>repo.SaveUser(new UserAccount {Login="short",FullName="Bad",Role=UserRole.Manager},"short",admin),"new account requires a strong-enough password");
    var eightyCharacters=new string('x',80);
    repo.SaveUser(new UserAccount {Login=eightyCharacters,FullName="Long login",Role=UserRole.Manager},"LongLoginPass-88",admin);
    Check(repo.Authenticate(eightyCharacters+"extra","LongLoginPass-88") is null,"overlong login is never authenticated as a truncated valid login");

    var validProduct=new Product {Name="Denied",Unit="шт",PurchasePrice=1,SalePrice=2};
    Denied(()=>repo.SaveProduct(validProduct,manager),"ordinary manager cannot change products");
    Denied(()=>repo.SaveProduct(validProduct,sales),"sales manager cannot change products");
    var validWarehouse=new WarehouseEntity {Name="Denied",Address="Minsk",ManagerName="Person",ManagerContact="Phone"};
    Denied(()=>repo.SaveWarehouse(validWarehouse,store),"storekeeper cannot change warehouses");
    Denied(()=>repo.SaveWarehouse(validWarehouse,store with {Role=UserRole.Admin}),"repository takes actor role from SQL, not the caller's role value");
    Denied(()=>repo.SaveCounterparty(new Counterparty {Name="Denied",IsSupplier=true},sales),"sales manager cannot change counterparties");
    Invalid(()=>repo.SaveProduct(new Product {Name="Zero",Unit="шт",PurchasePrice=0,SalePrice=1},store),"zero purchase price rejected");
    Invalid(()=>repo.SaveProduct(new Product {Name="Zero",Unit="шт",PurchasePrice=1,SalePrice=0},store),"zero sale price rejected");
    Invalid(()=>repo.SaveWarehouse(new WarehouseEntity {Name="",Address="A",ManagerName="N",ManagerContact="C"},admin),"warehouse name required");
    Invalid(()=>repo.SaveWarehouse(new WarehouseEntity {Name="N",Address="",ManagerName="N",ManagerContact="C"},admin),"warehouse address required");
    Invalid(()=>repo.SaveWarehouse(new WarehouseEntity {Name="N",Address="A",ManagerName=" ",ManagerContact="C"},admin),"warehouse manager required");
    Invalid(()=>repo.SaveWarehouse(new WarehouseEntity {Name="N",Address="A",ManagerName="N",ManagerContact=""},admin),"warehouse contact required");
    SqlConstraint(cs,"INSERT dbo.Products(Name,Unit,PurchasePrice,SalePrice) VALUES (N'zero',N'шт',0,1)","SQL rejects zero purchase price");
    SqlConstraint(cs,"INSERT dbo.Products(Name,Unit,PurchasePrice,SalePrice) VALUES (N'zero',N'шт',1,0)","SQL rejects zero sale price");
    SqlConstraint(cs,"INSERT dbo.Warehouses(Name,Address,ManagerName,ManagerContact) VALUES (N'W',N'',N'N',N'C')","SQL rejects missing warehouse fields");
    SqlConstraint(cs,"INSERT dbo.Warehouses(Name,Address,ManagerName,ManagerContact) VALUES (N'W',NCHAR(9)+NCHAR(160),N'N',N'C')","SQL rejects Unicode-whitespace-only warehouse fields");
    // Если сам SQL-аудит не записался, изменение справочника должно откатиться целиком.
    using (var c=new SqlConnection(cs))
    {
        c.Open(); using var q=c.CreateCommand();
        q.CommandText=@"CREATE TRIGGER dbo.TestAuditFailure ON dbo.AuditEvents AFTER INSERT AS
            IF EXISTS (SELECT 1 FROM inserted WHERE Details LIKE N'%force-audit-failure%') THROW 51050, N'Test audit failure', 1;";
        q.ExecuteNonQuery();
    }
    try
    {
        try { repo.SaveProduct(new Product {Name="force-audit-failure",Unit="шт",PurchasePrice=1,SalePrice=2},store); throw new Exception("FAILED: audit failure accepted"); }
        catch (SqlException ex) when (ex.Number==51050) { checks++; Console.WriteLine("OK: SQL audit insertion failure rejects mutation"); }
        Check(!repo.GetProducts().Any(p=>p.Name=="force-audit-failure"),"mutation and audit reference are atomically rolled back");
    }
    finally
    {
        using var c=new SqlConnection(cs); c.Open(); using var q=c.CreateCommand(); q.CommandText="DROP TRIGGER dbo.TestAuditFailure"; q.ExecuteNonQuery();
    }

    repo.SaveProduct(new Product {Name="Тест 100%",Unit="шт",PurchasePrice=10.25m,SalePrice=20.40m},store);
    repo.SaveProduct(new Product {Name="Другая позиция",Unit="шт",PurchasePrice=5m,SalePrice=9m},admin);
    repo.SaveWarehouse(new WarehouseEntity {Name="Склад A",Address="Минск",ManagerName="Иванов",ManagerContact="+375"},admin);
    repo.SaveWarehouse(new WarehouseEntity {Name="Склад B",Address="Брест",ManagerName="Петров",ManagerContact="+375 29"},admin);
    repo.SaveCounterparty(new Counterparty {Name="Поставщик",IsSupplier=true},admin);
    repo.SaveCounterparty(new Counterparty {Name="Покупатель",IsSupplier=false,Address="Брест",Unp="123"},admin);
    var products=repo.GetProducts(); var product=products.Single(p=>p.Name=="Тест 100%"); var other=products.Single(p=>p.Name=="Другая позиция");
    var warehouses=repo.GetWarehouses(); var wa=warehouses.Single(w=>w.Name=="Склад A"); var wb=warehouses.Single(w=>w.Name=="Склад B");
    var parties=repo.GetCounterparties(); var supplier=parties.Single(p=>p.IsSupplier); var buyer=parties.Single(p=>!p.IsSupplier);
    repo.SaveOrganization(new PartyDetails {Name="Тестовая организация",Address="Минск",Unp="123456789"},admin);
    var fixtureInvoiceDate=repo.GetServerTime().Date;
    int Conduct(WarehouseRepository repository, OperationInput op, AppUser user)
    {
        if (op.Kind!=OperationKind.Shipment) return repository.CreateOperation(op,user);
        return repository.CreateBatch(new BatchOperationInput(op.Kind,op.FromWarehouseId,op.ToWarehouseId,op.CounterpartyId,op.Comment,
            [new OperationItemInput(op.ProductId,op.Quantity,1m)],
            new InvoiceDraft {Series="ТЕСТ",UseAutomaticNumber=true,Date=fixtureInvoiceDate,
                Recipient=new PartyDetails {Name=buyer.Name,Address=buyer.Address,Unp=buyer.Unp}}),user).BatchId;
    }
    Check(wa.Address=="Минск" && wa.ManagerName=="Иванов" && wa.ManagerContact=="+375","warehouse information persists");
    Check(product.PurchasePrice==10.25m && product.SalePrice==20.40m,"BYN prices persist");
    var receipt=new OperationInput(OperationKind.Receipt,product.Id,10,null,wa.Id,supplier.Id,"поставка");
    var shipment=new OperationInput(OperationKind.Shipment,product.Id,3,wa.Id,null,buyer.Id,"продажа");
    var transfer=new OperationInput(OperationKind.Transfer,product.Id,2,wa.Id,wb.Id,null,"перевод");
    var beforeDenied=repo.GetAuditEntries(10000).Count;
    Denied(()=>repo.CreateOperation(receipt,manager),"ordinary manager cannot conduct receipts");
    Denied(()=>repo.CreateOperation(shipment,manager),"ordinary manager cannot conduct shipments");
    Denied(()=>repo.CreateOperation(transfer,manager),"ordinary manager cannot conduct transfers");
    Denied(()=>repo.CreateOperation(shipment,store),"storekeeper cannot conduct sales");
    Denied(()=>repo.CreateOperation(receipt,sales),"sales manager cannot conduct receipts");
    Denied(()=>repo.CreateOperation(transfer,sales),"sales manager cannot conduct transfers");
    Check(repo.GetAuditEntries(10000).Count==beforeDenied && repo.GetOperations(new OperationFilter()).Count==0,"denied mutations neither change data nor create success events");
    repo.CreateOperation(receipt,store); Conduct(repo,shipment,sales); repo.CreateOperation(transfer,store);
    repo.CreateOperation(new OperationInput(OperationKind.Receipt,other.Id,2,null,wa.Id,supplier.Id,"admin receipt"),admin);
    Conduct(repo,new OperationInput(OperationKind.Shipment,other.Id,1,wa.Id,null,buyer.Id,"admin shipment"),admin);
    repo.CreateOperation(new OperationInput(OperationKind.Transfer,other.Id,1,wa.Id,wb.Id,null,"admin transfer"),admin);
    var stocks=repo.GetStocks(new StockFilter());
    Check(stocks.Single(s=>s.ProductId==product.Id && s.WarehouseId==wa.Id).Quantity==5 && stocks.Single(s=>s.ProductId==product.Id && s.WarehouseId==wb.Id).Quantity==2,"receipt/sales/transfer balances and admin superuser rights");
    Check(repo.GetStocks(new StockFilter {WarehouseId=wb.Id,Search="100%"}).Single().Quantity==2,"stock filters and literal percent search");
    var now=repo.GetServerTime();
    Check(repo.GetOperations(new OperationFilter {FromInclusive=now.Date,ToExclusive=now.Date.AddDays(1),Kind=OperationKind.Shipment,CounterpartyId=buyer.Id,ProductId=product.Id}).Count==1,"operation filters");
    var sold=repo.GetOperations(new OperationFilter {Kind=OperationKind.Shipment,ProductId=product.Id}).Single();
    Check(repo.GetOperations(new OperationFilter {Kind=OperationKind.Shipment,ProductId=product.Id,FromInclusive=sold.OccurredAt}).Count==1,"start timestamp inclusive");
    Check(repo.GetOperations(new OperationFilter {Kind=OperationKind.Shipment,ProductId=product.Id,ToExclusive=sold.OccurredAt}).Count==0,"end timestamp exclusive");
    Check(repo.GetOperations(new OperationFilter {Search="%' OR 1=1 --"}).Count==0,"search treats SQL injection as literal text");
    Check(repo.GetStocks(new StockFilter {LastMovementFromInclusive=now.Date.AddDays(1)}).Count==0,"last movement filter");
    var before=repo.GetOperations(new OperationFilter()).Count; var auditBefore=repo.GetAuditEntries(10000).Count;
    try { Conduct(repo,shipment with {Quantity=6},sales); throw new Exception("FAILED: overselling accepted"); }
    catch (InvalidOperationException) { checks++; Console.WriteLine("OK: insufficient stock rejected"); }
    try { repo.CreateOperation(transfer with {ToWarehouseId=999999},store); throw new Exception("FAILED: invalid destination accepted"); }
    catch (SqlException) { checks++; Console.WriteLine("OK: invalid destination rejected"); }
    Check(repo.GetOperations(new OperationFilter()).Count==before && repo.GetAuditEntries(10000).Count==auditBefore && repo.GetStocks(new StockFilter()).Single(s=>s.ProductId==product.Id && s.WarehouseId==wa.Id).Quantity==5,"failed operations roll back balances and audit events atomically");
    try { repo.SaveProduct(new Product {Id=product.Id,Name=product.Name,Unit="кг",PurchasePrice=product.PurchasePrice,SalePrice=product.SalePrice},store); throw new Exception("FAILED: unit changed despite history"); }
    catch (InvalidOperationException) { checks++; Console.WriteLine("OK: unit cannot change after movements"); }
    try { repo.DeleteProduct(product.Id,store); throw new Exception("FAILED: historical product deleted"); }
    catch (InvalidOperationException) { checks++; Console.WriteLine("OK: historical data protected by foreign keys"); }
    var movements=repo.GetMovements(new[] {product.Id},repo.GetServerTime().AddSeconds(1));
    Check(repo.GetMovements(Enumerable.Range(1_000_000,501).Prepend(product.Id),repo.GetServerTime().AddSeconds(1)).Count==movements.Count,"turnover query handles more than 500 product IDs");
    Check(TurnoverCalculator.Calculate(new[] {product},movements,now.Date,now.AddSeconds(1)).Single().Sold==3,"turnover reads persisted shipment ledger");
    var sameStreamRepo=new WarehouseRepository(cs,auditDir,repo.AuditStreamId);
    var tasks=Enumerable.Range(0,12).Select(i=>Task.Run(()=>
    {
        try { Conduct(i%2==0 ? repo : sameStreamRepo,shipment with {Quantity=1,Comment="parallel"},sales); return true; }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Недостаточно товара")) { return false; }
    })).ToArray();
    Task.WaitAll(tasks);
    Check(tasks.Count(t=>t.Result)==5,"parallel sales cannot oversell");
    Check(repo.GetStocks(new StockFilter {IncludeZero=true}).Single(s=>s.ProductId==product.Id && s.WarehouseId==wa.Id).Quantity==0,"parallel balances consistent");
    Check(repo.SyncAuditFile().State==AuditFileState.Verified,"parallel writes leave verifiable file");

    // Многотоварные операции и обязательная накладная: отдельные остатки и снимок реквизитов.
    Denied(()=>repo.SaveOrganization(new PartyDetails {Name="Denied",Address="Denied"},store),"only admin changes own organization");
    repo.SaveProduct(new Product {Name="Товар группы A",Unit="шт",PurchasePrice=50,SalePrice=100,UnitWeightKg=2.5m,UnitsPerCargoPlace=10},store);
    repo.SaveProduct(new Product {Name="Товар группы B",Unit="шт",PurchasePrice=3,SalePrice=7.50m},store);
    var batchA=repo.GetProducts().Single(p=>p.Name=="Товар группы A"); var batchB=repo.GetProducts().Single(p=>p.Name=="Товар группы B");
    Check(batchA.UnitWeightKg==2.5m && batchA.UnitsPerCargoPlace==10,"product weight and package metadata persist");
    Invalid(()=>repo.CreateBatch(new BatchOperationInput(OperationKind.Receipt,null,wa.Id,supplier.Id,"",[]),store),"empty group rejected");
    Invalid(()=>repo.CreateBatch(new BatchOperationInput(OperationKind.Receipt,null,wa.Id,supplier.Id,"",Enumerable.Range(0,201).Select(_=>new OperationItemInput(batchA.Id,1)).ToArray()),store),
        "more than 200 positions rejected");
    var incoming=repo.CreateBatch(new BatchOperationInput(OperationKind.Receipt,null,wa.Id,supplier.Id,"групповой приход",
        [new(batchA.Id,10),new(batchB.Id,9)]),store);
    var moving=repo.CreateBatch(new BatchOperationInput(OperationKind.Transfer,wa.Id,wb.Id,null,"групповое перемещение",
        [new(batchA.Id,2),new(batchB.Id,3)]),store);
    Check(repo.GetOperationItems(incoming.BatchId,null).Count==2 && repo.GetOperationItems(moving.BatchId,null).Count==2,"multiple receipt and transfer lines are grouped");
    Check(repo.GetStocks(new StockFilter()).Single(x=>x.ProductId==batchA.Id && x.WarehouseId==wb.Id).Quantity==2
        && repo.GetStocks(new StockFilter()).Single(x=>x.ProductId==batchB.Id && x.WarehouseId==wb.Id).Quantity==3,"multi-item transfer updates both products");
    InvoiceDraft Draft() => new() {Series="ВНТ",UseAutomaticNumber=true,Date=fixtureInvoiceDate,PriceMode=VatPriceMode.Included,
        Recipient=new PartyDetails {Name=buyer.Name,Address=buyer.Address,Unp=buyer.Unp},
        Payer=new PartyDetails {Name=buyer.Name,Address=buyer.Address,Unp=buyer.Unp},PayerCounterpartyId=buyer.Id,
        Vehicle="Грузовик 1234",Driver="Иванов И.И.",WaybillNumber="77",LoadingPoint="Минск",UnloadingPoint="Брест",TransportPrice=12m,TransportVatRate=20m};
    var saleInput=new BatchOperationInput(OperationKind.Shipment,wa.Id,null,buyer.Id,"групповая продажа",
        [new(batchA.Id,3,100m,10m,20m,1,0.0075m,"тест"),new(batchB.Id,2,7.50m,0,null,2,0.002m)],Draft());
    Denied(()=>repo.CreateBatch(saleInput,store),"storekeeper cannot conduct batch sales");
    Denied(()=>repo.CreateBatch(saleInput,manager),"ordinary manager cannot conduct batch sales");
    Denied(()=>repo.CreateBatch(new BatchOperationInput(OperationKind.Receipt,null,wa.Id,supplier.Id,"",[new(batchA.Id,1)]),sales),"sales manager cannot conduct batch receipts");
    try { repo.CreateBatch(saleInput with {Invoice=null},sales); throw new Exception("FAILED: sale without invoice accepted"); }
    catch(ArgumentException) { checks++; Console.WriteLine("OK: batch sale requires invoice"); }
    try { repo.CreateOperation(new OperationInput(OperationKind.Shipment,batchA.Id,1,wa.Id,null,buyer.Id,""),sales); throw new Exception("FAILED: legacy sale bypassed invoice"); }
    catch(InvalidOperationException ex) when (ex.Message.Contains("накладной")) { checks++; Console.WriteLine("OK: legacy entry point cannot bypass invoice"); }
    var changedRecipient=Draft(); changedRecipient.Recipient.Name="Подставной получатель";
    try { repo.CreateBatch(saleInput with {Invoice=changedRecipient},sales); throw new Exception("FAILED: sales manager changed recipient details"); }
    catch(InvalidOperationException ex) when(ex.Message.Contains("Реквизиты получателя")) { checks++; Console.WriteLine("OK: sales manager must use administrator's recipient details"); }
    var changedPayer=Draft(); changedPayer.Payer.Address="Чужой адрес";
    try { repo.CreateBatch(saleInput with {Invoice=changedPayer},sales); throw new Exception("FAILED: sales manager changed payer details"); }
    catch(InvalidOperationException ex) when(ex.Message.Contains("плательщика")) { checks++; Console.WriteLine("OK: sales manager must choose an approved payer"); }
    var changedNoVat=Draft(); changedNoVat.PriceMode=VatPriceMode.WithoutVat;
    Invalid(()=>repo.CreateBatch(saleInput with {Invoice=changedNoVat},sales),"without-VAT sale rejects stale line or transport VAT rates");
    var groupedSale=repo.CreateBatch(saleInput,sales); var invoice=groupedSale.Invoice ?? throw new Exception("FAILED: no invoice");
    Check(invoice.Lines.Count==3 && invoice.Lines[2].IsService && invoice.Lines[2].Quantity is null,"invoice contains two goods and a non-stock transport service");
    Check(invoice.Lines[0].UnitPrice==90m && invoice.Lines[0].GrossAmount==270m && invoice.Lines[0].VatAmount==45m
        && invoice.TotalNet==250m && invoice.TotalVat==47m && invoice.TotalGross==297m,"discount, included VAT, transport and totals calculated per line");
    Check(invoice.Lines[1].VatRate is null && invoice.Lines[1].VatAmount is null && invoice.Sender.Unp=="123456789","without VAT is distinct from zero and sender comes from profile");
    var grouped=repo.GetOperationBatches(new OperationFilter {ProductId=batchA.Id,Kind=OperationKind.Shipment}).Single();
    Check(grouped.BatchId==groupedSale.BatchId && grouped.ItemCount==2 && grouped.InvoiceId==invoice.Id,"product filter finds whole multi-line sale and its invoice");
    Check(repo.GetOperationItems(grouped.BatchId,null).Count==2 && repo.GetOperations(new OperationFilter {ProductId=batchB.Id,Kind=OperationKind.Shipment}).Count==1,
        "group details show all items while per-product history stays available");
    Check(repo.GetOperationBatches(new OperationFilter {Search="Товар группы B"}).Any(x=>x.BatchId==groupedSale.BatchId),"group search finds any product in group");
    Check(repo.GetOperationBatches(new OperationFilter {Search=invoice.Number}).Any(x=>x.BatchId==groupedSale.BatchId)
        && repo.GetOperations(new OperationFilter {Search=invoice.Number}).Any(x=>x.BatchId==groupedSale.BatchId),
        "invoice number search reaches both groups and individual goods");
    var savedInvoice=repo.GetInvoice(groupedSale.BatchId)!;
    Check(savedInvoice.Number==invoice.Number && savedInvoice.Recipient.Address=="Брест" && savedInvoice.Vehicle=="Грузовик 1234" && savedInvoice.TotalGross==297m,"invoice data round trips through SQL");
    var groupAudit=repo.GetAuditEntries(10000).Single(x=>x.Entity=="Групповая складская операция" && x.EntityId==groupedSale.BatchId.ToString());
    Check(groupAudit.Details.Contains("Товар группы A") && groupAudit.Details.Contains("Товар группы B")
        && groupAudit.Details.Contains("Грузовик 1234"),"group audit captures both positions and the invoice snapshot");
    var nextBeforeAuditFailure=repo.GetSuggestedInvoiceNumber();
    var stockBeforeAuditFailure=repo.GetStocks(new StockFilter()).Single(x=>x.ProductId==batchA.Id && x.WarehouseId==wa.Id).Quantity;
    var groupsBeforeAuditFailure=repo.GetOperationBatches(new OperationFilter()).Count;
    using (var c=new SqlConnection(cs))
    {
        c.Open(); using var q=c.CreateCommand();
        q.CommandText=@"CREATE TRIGGER dbo.TestBatchAuditFailure ON dbo.AuditEvents AFTER INSERT AS
            IF EXISTS (SELECT 1 FROM inserted WHERE Details LIKE N'%force-batch-audit-failure%') THROW 51051, N'Test batch audit failure', 1;";
        q.ExecuteNonQuery();
    }
    try
    {
        try { repo.CreateBatch(saleInput with {Comment="force-batch-audit-failure",Items=[new(batchA.Id,1,100)],Invoice=Draft()},sales); throw new Exception("FAILED: batch audit failure accepted"); }
        catch(SqlException ex) when(ex.Number==51051) { checks++; Console.WriteLine("OK: SQL audit failure rejects entire multi-item operation"); }
        Check(repo.GetSuggestedInvoiceNumber()==nextBeforeAuditFailure
            && repo.GetStocks(new StockFilter()).Single(x=>x.ProductId==batchA.Id && x.WarehouseId==wa.Id).Quantity==stockBeforeAuditFailure
            && repo.GetOperationBatches(new OperationFilter()).Count==groupsBeforeAuditFailure,
            "audit failure rolls back balances, group, invoice and automatic numbering");
    }
    finally { using var c=new SqlConnection(cs); c.Open(); using var q=c.CreateCommand(); q.CommandText="DROP TRIGGER dbo.TestBatchAuditFailure"; q.ExecuteNonQuery(); }
    batchA.Name="Переименованный товар"; batchA.SalePrice=120; repo.SaveProduct(batchA,store);
    Check(repo.GetInvoice(groupedSale.BatchId)!.Lines[0].Name=="Товар группы A" && repo.GetInvoice(groupedSale.BatchId)!.Lines[0].PriceBeforeDiscount==100m
        && repo.GetOperationItems(groupedSale.BatchId,null)[0].ProductName=="Товар группы A","historical document and movement names/prices do not change with product card");
    Check(repo.GetOperationBatches(new OperationFilter {Search="Товар группы A"}).Any(x=>x.BatchId==groupedSale.BatchId)
        && repo.GetOperations(new OperationFilter {Search="Товар группы A"}).Any(x=>x.BatchId==groupedSale.BatchId),
        "search finds historical product snapshot after rename");
    var balanceA=repo.GetStocks(new StockFilter()).Single(x=>x.ProductId==batchA.Id && x.WarehouseId==wa.Id).Quantity;
    var beforeBatchCount=repo.GetOperationBatches(new OperationFilter()).Count; var beforeAuditCount=repo.GetAuditEntries(10000).Count;
    try { repo.CreateBatch(saleInput with {Items=[new(batchA.Id,1,100),new(batchB.Id,999,7.5m)],Invoice=Draft()},sales); throw new Exception("FAILED: partial stock shortage accepted"); }
    catch(InvalidOperationException ex) when(ex.Message.Contains("Недостаточно")) { checks++; Console.WriteLine("OK: shortage in second line rejects batch"); }
    Check(repo.GetStocks(new StockFilter()).Single(x=>x.ProductId==batchA.Id && x.WarehouseId==wa.Id).Quantity==balanceA
        && repo.GetOperationBatches(new OperationFilter()).Count==beforeBatchCount && repo.GetAuditEntries(10000).Count==beforeAuditCount,
        "failed multi-line sale rolls back first line, group, invoice and audit");
    var duplicate=Draft(); duplicate.UseAutomaticNumber=false; duplicate.Series=invoice.Series; duplicate.Number=invoice.Number;
    try { repo.CreateBatch(saleInput with {Items=[new(batchA.Id,1,100)],Invoice=duplicate},sales); throw new Exception("FAILED: duplicate invoice accepted"); }
    catch(InvalidOperationException ex) when(ex.Message.Contains("уже существует")) { checks++; Console.WriteLine("OK: duplicate series and number rejected"); }
    Check(repo.GetStocks(new StockFilter()).Single(x=>x.ProductId==batchA.Id && x.WarehouseId==wa.Id).Quantity==balanceA,"duplicate invoice rolls back stock");
    var parallelBatches=Enumerable.Range(0,4).Select(_=>Task.Run(()=>repo.CreateBatch(saleInput with {
        Items=[new(batchA.Id,1,100),new(batchB.Id,1,7.5m)],Invoice=Draft()},sales))).ToArray();
    Task.WaitAll(parallelBatches);
    Check(parallelBatches.Select(x=>x.Result.Invoice!.Number).Distinct().Count()==4,"concurrent automatic invoice numbers are unique");
    Check(repo.GetStocks(new StockFilter {IncludeZero=true}).Single(x=>x.ProductId==batchB.Id && x.WarehouseId==wa.Id).Quantity==0,
        "parallel multi-line sales update stock consistently");
    var withoutVat=Draft(); withoutVat.PriceMode=VatPriceMode.WithoutVat; withoutVat.Series="";
    withoutVat.TransportPrice=null; withoutVat.TransportVatRate=null;
    var nonTaxable=repo.CreateBatch(saleInput with {Items=[new(batchA.Id,1,100,0,null)],Invoice=withoutVat},sales).Invoice!;
    Check(nonTaxable.PriceMode==VatPriceMode.WithoutVat && nonTaxable.Series=="" && nonTaxable.Lines[0].VatAmount is null
        && nonTaxable.TotalGross==100m && repo.GetInvoice(nonTaxable.BatchId)!.PriceMode==VatPriceMode.WithoutVat,
        "whole invoice without VAT and optional series persist");
    // Несколько конкурирующих групп не могут частично продать товары или уйти в минус.
    repo.SaveProduct(new Product {Name="Конкурент A",Unit="шт",PurchasePrice=1,SalePrice=2},store);
    repo.SaveProduct(new Product {Name="Конкурент B",Unit="шт",PurchasePrice=1,SalePrice=2},store);
    var concurrentA=repo.GetProducts().Single(x=>x.Name=="Конкурент A"); var concurrentB=repo.GetProducts().Single(x=>x.Name=="Конкурент B");
    repo.CreateBatch(new BatchOperationInput(OperationKind.Receipt,null,wa.Id,supplier.Id,"параллельный резерв",
        [new(concurrentA.Id,2),new(concurrentB.Id,2)]),store);
    var sequenceBeforeOversell=repo.GetSuggestedInvoiceNumber();
    var oversellGroups=Enumerable.Range(0,5).Select(_=>Task.Run(()=>
    {
        try { repo.CreateBatch(new BatchOperationInput(OperationKind.Shipment,wa.Id,null,buyer.Id,"параллельная группа",
            [new(concurrentA.Id,1,2),new(concurrentB.Id,1,2)],Draft()),sales); return true; }
        catch(InvalidOperationException ex) when(ex.Message.Contains("Недостаточно товара")) { return false; }
    })).ToArray();
    Task.WaitAll(oversellGroups);
    var concurrentStocks=repo.GetStocks(new StockFilter {IncludeZero=true});
    Check(oversellGroups.Count(x=>x.Result)==2 && concurrentStocks.Single(x=>x.ProductId==concurrentA.Id && x.WarehouseId==wa.Id).Quantity==0
        && concurrentStocks.Single(x=>x.ProductId==concurrentB.Id && x.WarehouseId==wa.Id).Quantity==0
        && repo.GetSuggestedInvoiceNumber()==sequenceBeforeOversell+2,
        "competing multi-item sales cannot oversell or consume numbers for failed transactions");

    // Встречные связи покупателей и плательщиков не должны создавать циклы блокировок.
    repo.SaveCounterparty(new Counterparty {Name="Покупатель 2",IsSupplier=false,Address="Гомель",Unp="456"},admin);
    var buyer2=repo.GetCounterparties().Single(x=>x.Name=="Покупатель 2");
    repo.CreateBatch(new BatchOperationInput(OperationKind.Receipt,null,wa.Id,supplier.Id,"резерв",
        [new(batchA.Id,5),new(batchB.Id,5)]),store);
    repo.CreateBatch(new BatchOperationInput(OperationKind.Receipt,null,wb.Id,supplier.Id,"резерв",
        [new(batchA.Id,5),new(batchB.Id,5)]),store);
    var beforeTransfers=repo.GetStocks(new StockFilter {IncludeZero=true}).Where(x=>(x.ProductId==batchA.Id || x.ProductId==batchB.Id)
        && (x.WarehouseId==wa.Id || x.WarehouseId==wb.Id)).ToDictionary(x=>(x.ProductId,x.WarehouseId),x=>x.Quantity);
    var opposing=new[] {
        Task.Run(()=>repo.CreateBatch(new BatchOperationInput(OperationKind.Transfer,wa.Id,wb.Id,null,"встречное 1",[new(batchA.Id,1),new(batchB.Id,1)]),store)),
        Task.Run(()=>repo.CreateBatch(new BatchOperationInput(OperationKind.Transfer,wb.Id,wa.Id,null,"встречное 2",[new(batchB.Id,1),new(batchA.Id,1)]),store))
    };
    Task.WaitAll(opposing);
    var afterTransfers=repo.GetStocks(new StockFilter {IncludeZero=true}).Where(x=>beforeTransfers.ContainsKey((x.ProductId,x.WarehouseId)))
        .ToDictionary(x=>(x.ProductId,x.WarehouseId),x=>x.Quantity);
    Check(beforeTransfers.All(x=>afterTransfers[x.Key]==x.Value) && opposing.All(x=>repo.GetOperationItems(x.Result.BatchId,null).Count==2),
        "opposite multi-item transfers acquire locks safely and preserve both balances");
    PartyDetails Party(Counterparty p) => new() {Name=p.Name,Address=p.Address,Unp=p.Unp};
    InvoiceDraft CrossDraft(Counterparty recipient,Counterparty payer) => new() {PriceMode=VatPriceMode.WithoutVat,
        Date=fixtureInvoiceDate,Series="ПАР",UseAutomaticNumber=true,Recipient=Party(recipient),RecipientCounterpartyId=recipient.Id,
        Payer=Party(payer),PayerCounterpartyId=payer.Id};
    var crossed=new[] {
        Task.Run(()=>repo.CreateBatch(new BatchOperationInput(OperationKind.Shipment,wa.Id,null,buyer.Id,"",
            [new(batchA.Id,1,100)],CrossDraft(buyer2,buyer)),sales)),
        Task.Run(()=>repo.CreateBatch(new BatchOperationInput(OperationKind.Shipment,wb.Id,null,buyer2.Id,"",
            [new(batchB.Id,1,7.5m)],CrossDraft(buyer,buyer2)),sales))
    };
    Task.WaitAll(crossed);
    Check(crossed[0].Result.Invoice!.Recipient.Name==buyer2.Name && crossed[1].Result.Invoice!.Recipient.Name==buyer.Name
        && crossed[0].Result.Invoice!.Number!=crossed[1].Result.Invoice!.Number,
        "different recipient and payer can be selected from counterparties in concurrent sales");
    var end=repo.GetServerTime().AddSeconds(2);
    var batchMovements=repo.GetMovements([batchA.Id,batchB.Id],end);
    var turnover=TurnoverCalculator.Calculate([batchA,batchB],batchMovements,now.Date,end);
    Check(turnover.Single(x=>x.ProductId==batchA.Id).Sold==9 && turnover.Single(x=>x.ProductId==batchB.Id).Sold==7,
        "turnover counts each grouped shipment line once and excludes transport service");
    var beforeOrganization=repo.GetInvoice(groupedSale.BatchId)!.Sender.Name;
    repo.SaveOrganization(new PartyDetails {Name="Обновлённая организация",Address="Минск",Unp="987654321"},admin);
    buyer.Address="Витебск"; repo.SaveCounterparty(buyer,admin);
    Check(repo.GetInvoice(groupedSale.BatchId)!.Sender.Name==beforeOrganization
        && repo.GetInvoice(groupedSale.BatchId)!.Recipient.Address=="Брест",
        "organization and counterparty edits do not rewrite historical invoices");
    var oldWarehouseName=wa.Name; var oldBuyerName=buyer.Name;
    wa.Name="Склад после переименования"; repo.SaveWarehouse(wa,admin);
    buyer.Name="Покупатель после переименования"; repo.SaveCounterparty(buyer,admin);
    repo.SaveUser(new UserAccount {Id=sales.Id,Login="sales2",FullName="ФИО после переименования",Role=UserRole.SalesManager},null,admin);
    var oldGroup=repo.GetOperationBatches(new OperationFilter {Search=oldWarehouseName}).Single(x=>x.BatchId==groupedSale.BatchId);
    var oldMovement=repo.GetOperationItems(groupedSale.BatchId,null).First();
    Check(oldGroup.FromWarehouse==oldWarehouseName && oldGroup.CounterpartyName==oldBuyerName && oldGroup.UserName=="Сотрудник продаж"
        && oldMovement.FromWarehouse==oldWarehouseName && oldMovement.CounterpartyName==oldBuyerName && oldMovement.UserName=="Сотрудник продаж",
        "group and individual history preserve warehouse, counterparty and user names at operation time");

    // Проверка текущей роли в БД и мягкого удаления с сохранением истории.
    repo.SaveUser(new UserAccount {Id=sales.Id,Login="sales2",FullName="Сотрудник продаж",Role=UserRole.Manager},null,admin);
    Denied(()=>repo.CreateOperation(shipment,sales),"role revocation affects an already-open session");
    repo.SaveUser(new UserAccount {Id=sales.Id,Login="sales2",FullName="Сотрудник продаж",Role=UserRole.SalesManager},null,admin);
    repo.DeleteUser(sales.Id,admin);
    Check(repo.Authenticate("sales2","NewSalesPassword-88") is null,"deleted account cannot log in");
    Denied(()=>repo.CreateOperation(shipment,sales),"deleted account cannot mutate using stale session");
    Check(repo.GetOperations(new OperationFilter {Kind=OperationKind.Shipment,ProductId=product.Id}).Any(x=>x.UserName=="Сотрудник продаж"),"deleting user preserves operation history");
    try { repo.DeleteUser(admin.Id,admin); throw new Exception("FAILED: self deletion allowed"); }
    catch (InvalidOperationException) { checks++; Console.WriteLine("OK: self deletion blocked"); }

    // Файл — побайтовая копия только событий собственного потока; SQL остаётся эталоном.
    Check(repo.SyncAuditFile().State==AuditFileState.Verified,"audit verified before tamper tests");
    var saved=File.ReadAllBytes(repo.AuditFilePath); var corrupt=(byte[])saved.Clone(); corrupt[0]=corrupt[0]==(byte)'{' ? (byte)'[' : (byte)'{';
    File.WriteAllBytes(repo.AuditFilePath,corrupt);
    Check(repo.SyncAuditFile().State==AuditFileState.Diverged,"modified log bytes detected");
    var beforeTampered=File.ReadAllBytes(repo.AuditFilePath);
    repo.SaveProduct(new Product {Name="Сохранено при повреждённом журнале",Unit="шт",PurchasePrice=1,SalePrice=2},store);
    Check(repo.AuditStatus.State==AuditFileState.Diverged && File.ReadAllBytes(repo.AuditFilePath).SequenceEqual(beforeTampered),"successful SQL change recorded without overwriting tampered evidence");
    Check(repo.GetAuditEntries(10000).Any(e=>e.Action=="Добавлен товар" && e.ActorRole=="Кладовщик" && e.Details.Contains("Сохранено при повреждённом журнале")),"SQL audit identifies legitimate actor, role and change");
    File.WriteAllBytes(repo.AuditFilePath,saved);
    Check(repo.SyncAuditFile().State==AuditFileState.Verified,"restored file catches up with committed pending SQL events");
    saved=File.ReadAllBytes(repo.AuditFilePath);
    File.AppendAllText(repo.AuditFilePath,"fake\n",new UTF8Encoding(false));
    Check(repo.SyncAuditFile().State==AuditFileState.Diverged,"extra forged log entry detected");
    File.WriteAllBytes(repo.AuditFilePath,saved);
    var lastLineStart=Array.LastIndexOf(saved,(byte)'\n',saved.Length-2)+1;
    File.WriteAllBytes(repo.AuditFilePath,saved[..lastLineStart]);
    Check(repo.SyncAuditFile().State==AuditFileState.Diverged,"whole-line log truncation detected using SQL checkpoint");
    File.Delete(repo.AuditFilePath);
    Check(repo.SyncAuditFile().State==AuditFileState.Diverged,"deleted previously written log detected");
    File.WriteAllBytes(repo.AuditFilePath,saved);
    Check(repo.SyncAuditFile().State==AuditFileState.Verified,"restored authentic log verifies");
    var victim=repo.GetAuditEntries(10000).First(e=>e.StreamId==repo.AuditStreamId);
    void ChangeReference(string details)
    {
        using var c=new SqlConnection(cs); c.Open(); using var q=c.CreateCommand();
        q.CommandText="UPDATE dbo.AuditEvents SET Details=@details WHERE Id=@id";
        q.Parameters.Add("@details",SqlDbType.NVarChar,-1).Value=details; q.Parameters.Add("@id",SqlDbType.BigInt).Value=victim.Id; q.ExecuteNonQuery();
    }
    ChangeReference("{\"directSQL\":true}");
    Check(repo.SyncAuditFile().State==AuditFileState.Diverged && File.ReadAllBytes(repo.AuditFilePath).SequenceEqual(saved),"SQL reference change also produces discrepancy without falsely rewriting file");
    ChangeReference(victim.Details);
    Check(repo.SyncAuditFile().State==AuditFileState.Verified,"restoring the SQL reference restores verification");
    Check(!File.ReadAllText(repo.AuditFilePath).Contains("SalesPassword-99") && !repo.GetAuditEntries(10000).Any(e=>e.Details.Contains("NewSalesPassword-88")),"passwords never appear in audit file or SQL details");
    var second=new WarehouseRepository(cs,auditDir,Guid.NewGuid());
    Check(second.Authenticate("manager","123") is not null && second.AuditStatus.State==AuditFileState.Verified && second.AuditFilePath!=repo.AuditFilePath && repo.SyncAuditFile().State==AuditFileState.Verified,"independent client streams do not cause false tamper alerts");
    var blockedPath=Path.Combine(auditDir,"not-a-directory"); File.WriteAllText(blockedPath,"test");
    var unavailable=new WarehouseRepository(cs,blockedPath,Guid.NewGuid());
    Check(unavailable.Authenticate("manager","123") is not null && unavailable.AuditStatus.State==AuditFileState.Unavailable,"file failure reports warning without pretending committed login failed");
    File.Delete(blockedPath); Directory.CreateDirectory(blockedPath);
    Check(unavailable.SyncAuditFile().State==AuditFileState.Verified && File.Exists(unavailable.AuditFilePath),"unwritten committed events recover when file access returns");

    repo.ChangePassword(admin,"admin","new-password-123");
    Check(repo.Authenticate("admin","admin") is null && repo.Authenticate("admin","new-password-123") is not null,"password change persisted");
    DatabaseInitializer.Initialize(cs);
    Check(repo.Authenticate("admin","admin") is null && repo.Authenticate("admin","new-password-123") is not null,"initialization never restores demo passwords");
    var a2=new UserAccount {Login="admin2",FullName="Второй администратор",Role=UserRole.Admin};
    repo.SaveUser(a2,"AdminTwo-123",admin); var admin2=repo.Authenticate("admin2","AdminTwo-123")!;
    var simultaneous=new[] {
        Task.Run(()=> { try { repo.SaveUser(new UserAccount {Id=admin2.Id,Login=admin2.Login,FullName=admin2.FullName,Role=UserRole.Manager},null,admin); return true; } catch (UnauthorizedAccessException) { return false; } }),
        Task.Run(()=> { try { repo.SaveUser(new UserAccount {Id=admin.Id,Login=admin.Login,FullName=admin.FullName,Role=UserRole.Manager},null,admin2); return true; } catch (UnauthorizedAccessException) { return false; } })
    };
    Task.WaitAll(simultaneous);
    Check(simultaneous.Count(t=>t.Result)==1,"concurrent cross-demotions preserve an administrator and invalidate stale actor role");
    var survivor=simultaneous[0].Result ? admin : admin2;
    Check(repo.GetUsers(survivor).Count(u=>u.Role==UserRole.Admin)==1,"at least one active administrator remains");

    // Создаём базу именно по старой схеме: существующие неполные данные и пароли должны сохраниться.
    using (var master=new SqlConnection(new SqlConnectionStringBuilder(cs) {InitialCatalog="master"}.ConnectionString))
    {
        master.Open(); using var create=master.CreateCommand(); create.CommandText=$"CREATE DATABASE [{migrationDb}]"; create.ExecuteNonQuery();
    }
    using (var c=new SqlConnection(migrationCs))
    {
        c.Open(); using var tx=c.BeginTransaction(IsolationLevel.Serializable);
        using (var q=new SqlCommand(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"SchemaV1.sql")),c,tx)) q.ExecuteScalar();
        var (salt,hash)=PasswordHasher.Hash("RetainedAdmin-88");
        using (var q=new SqlCommand(@"INSERT dbo.WarehouseUsers(Login,FullName,Role,PasswordSalt,PasswordHash,PasswordIterations)
            VALUES(N'admin',N'Старый администратор',0,@salt,@hash,@iterations);
            INSERT dbo.Products(Name,Unit,PurchasePrice,SalePrice) VALUES(N'Старый товар',N'шт',0,0);
            INSERT dbo.Warehouses(Name,Address,ManagerName,ManagerContact) VALUES(N'Старый склад',NCHAR(9),N'',N'');
            INSERT dbo.Counterparties(Name,IsSupplier) VALUES(N'Старый поставщик',1);
            INSERT dbo.StockBalances(ProductId,WarehouseId,Quantity) VALUES(1,1,5);
            INSERT dbo.StockOperations(Kind,ProductId,Quantity,FromWarehouseId,ToWarehouseId,CounterpartyId,Comment,UserId)
                VALUES(0,1,5,NULL,1,1,N'Старая поставка',1);",c,tx))
        {
            q.Parameters.Add("@salt",SqlDbType.VarBinary,16).Value=salt; q.Parameters.Add("@hash",SqlDbType.VarBinary,32).Value=hash;
            q.Parameters.Add("@iterations",SqlDbType.Int).Value=PasswordHasher.Iterations; q.ExecuteNonQuery();
        }
        tx.Commit();
    }
    DatabaseInitializer.Initialize(migrationCs); DatabaseInitializer.Initialize(migrationCs);
    var legacy=new WarehouseRepository(migrationCs,auditDir,Guid.NewGuid());
    var oldAdmin=legacy.Authenticate("admin","RetainedAdmin-88") ?? throw new Exception("FAILED: old password lost during migration");
    Check(legacy.Authenticate("admin","admin") is null && legacy.GetUsers(oldAdmin).Count==1,"migration preserves existing users and password, does not reseed accounts");
    Check(legacy.GetProducts().Single().PurchasePrice==0 && legacy.GetWarehouses().Single().Address=="\t" && legacy.GetLegacyDataIssues()==new LegacyDataIssues(1,1),"legacy zero and empty fields preserved and flagged");
    Check(legacy.GetOperations(new OperationFilter()).Single().Comment=="Старая поставка" && legacy.GetStocks(new StockFilter()).Single().Quantity==5,"migration preserves old operation history and balances");
    var legacyGroup=legacy.GetOperationBatches(new OperationFilter()).Single();
    Check(legacyGroup.BatchId is null && legacyGroup.LegacyOperationId.HasValue && legacy.GetOperationItems(null,legacyGroup.LegacyOperationId).Count==1 && legacyGroup.InvoiceId is null,
        "legacy operation remains visible as one item without invented invoice");
    SqlConstraint(migrationCs,"INSERT dbo.Products(Name,Unit,PurchasePrice,SalePrice) VALUES (N'zero',N'шт',0,1)","migrated SQL rejects new zero price");
    SqlConstraint(migrationCs,"INSERT dbo.Warehouses(Name,Address,ManagerName,ManagerContact) VALUES (N'W',N'',N'N',N'C')","migrated SQL rejects new blank warehouse fields");
    var oldProduct=legacy.GetProducts().Single(); oldProduct.PurchasePrice=1; oldProduct.SalePrice=2; legacy.SaveProduct(oldProduct,oldAdmin);
    var oldWarehouse=legacy.GetWarehouses().Single(); oldWarehouse.Address="Минск"; oldWarehouse.ManagerName="ФИО"; oldWarehouse.ManagerContact="Телефон"; legacy.SaveWarehouse(oldWarehouse,oldAdmin);
    Check(!legacy.GetLegacyDataIssues().HasIssues,"legacy records can be corrected without data loss");
    // Освобождаем уже проверенные базы перед созданием следующей, чтобы тесту было достаточно небольшого SQL Server.
    SqlConnection.ClearAllPools();
    using (var master=new SqlConnection(new SqlConnectionStringBuilder(cs) {InitialCatalog="master"}.ConnectionString))
    {
        master.Open();
        foreach(var finished in new[] {db,migrationDb})
        { using var drop=master.CreateCommand(); drop.CommandText=$"ALTER DATABASE [{finished}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{finished}]"; drop.ExecuteNonQuery(); }
        // Отдельная миграция с версии 2 сохраняет пароли, роль продаж и существующий аудит.
        using var create=master.CreateCommand(); create.CommandText=$"CREATE DATABASE [{migrationV2Db}]"; create.ExecuteNonQuery();
    }
    using (var c=new SqlConnection(migrationV2Cs))
    {
        c.Open(); using var tx=c.BeginTransaction(IsolationLevel.Serializable);
        using (var q=new SqlCommand(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"SchemaV2.sql")),c,tx)) q.ExecuteScalar();
        var (salt,hash)=PasswordHasher.Hash("VersionTwo-Password88");
        using (var q=new SqlCommand(@"INSERT dbo.WarehouseUsers(Login,FullName,Role,PasswordSalt,PasswordHash,PasswordIterations)
            VALUES(N'admin',N'Администратор v2',0,@salt,@hash,@iterations),(N'sales',N'Продажи v2',3,@salt,@hash,@iterations);
            INSERT dbo.Products(Name,Unit,PurchasePrice,SalePrice) VALUES(N'Товар v2',N'шт',4,9);
            INSERT dbo.Warehouses(Name,Address,ManagerName,ManagerContact) VALUES(N'Склад v2',N'Адрес',N'ФИО',N'Телефон');
            INSERT dbo.Counterparties(Name,IsSupplier) VALUES(N'Поставщик v2',1);
            INSERT dbo.StockBalances(ProductId,WarehouseId,Quantity) VALUES(1,1,7);
            INSERT dbo.StockOperations(Kind,ProductId,Quantity,ToWarehouseId,CounterpartyId,Comment,UserId)
                VALUES(0,1,7,1,1,N'Операция v2',1);
            DECLARE @stream uniqueidentifier=NEWID();
            INSERT dbo.AuditStreams(StreamId,LastWrittenEventId) VALUES(@stream,0);
            INSERT dbo.AuditEvents(StreamId,ActorId,ActorLogin,ActorName,ActorRole,Action,Entity,EntityId,Details)
                VALUES(@stream,1,N'admin',N'Администратор v2',N'Администратор',N'Старая правка',N'Товар',N'1',N'сохранённый аудит');",c,tx))
        {
            q.Parameters.Add("@salt",SqlDbType.VarBinary,16).Value=salt; q.Parameters.Add("@hash",SqlDbType.VarBinary,32).Value=hash;
            q.Parameters.Add("@iterations",SqlDbType.Int).Value=PasswordHasher.Iterations; q.ExecuteNonQuery();
        }
        tx.Commit();
    }
    DatabaseInitializer.Initialize(migrationV2Cs); DatabaseInitializer.Initialize(migrationV2Cs);
    var fromV2=new WarehouseRepository(migrationV2Cs,auditDir,Guid.NewGuid());
    var adminV2=fromV2.Authenticate("admin","VersionTwo-Password88")!;
    var salesV2=fromV2.Authenticate("sales","VersionTwo-Password88")!;
    Check(adminV2 is not null && salesV2?.Role==UserRole.SalesManager && fromV2.GetUsers(adminV2).Count==2,
        "v2 to v3 migration preserves users, passwords and sales role");
    Check(fromV2.GetStocks(new StockFilter()).Single().Quantity==7 && fromV2.GetOperationBatches(new OperationFilter()).Single().BatchId is null
        && fromV2.GetAuditEntries().Any(x=>x.Action=="Старая правка" && x.Details=="сохранённый аудит"),
        "v2 to v3 migration preserves stock, old operations and audit events");
    Check(fromV2.GetProducts().Single().UnitWeightKg is null && fromV2.GetCounterparties().Single().Address==""
        && fromV2.GetOrganization().Name=="" && fromV2.GetSuggestedInvoiceNumber()==1,
        "v2 migration leaves unknown invoice details blank without inventing values");

    Console.WriteLine($"SQL Server integration checks passed: {checks}.");
    return 0;
}
finally
{
    SqlConnection.ClearAllPools();
    using var c=new SqlConnection(new SqlConnectionStringBuilder(cs) {InitialCatalog="master"}.ConnectionString); c.Open();
    foreach(var name in new[] {db,migrationDb,migrationV2Db})
    {
        using var q=c.CreateCommand();
        q.CommandText=$"IF DB_ID(@db) IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END";
        q.Parameters.AddWithValue("@db",name); q.CommandTimeout=30; q.ExecuteNonQuery();
    }
    if (Directory.Exists(auditDir)) Directory.Delete(auditDir,true);
}
