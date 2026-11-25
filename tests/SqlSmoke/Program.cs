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
var auditDir=Path.Combine(Path.GetTempPath(),"WarehouseSmoke_"+suffix);
var settings=new ConnectionSettings {Server=server,Database=db,IntegratedSecurity=false,SqlLogin=login,SqlPassword=password,TrustServerCertificate=true};
var cs=settings.BuildConnectionString();
var migrationCs=new SqlConnectionStringBuilder(cs) {InitialCatalog=migrationDb}.ConnectionString;
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
    repo.SaveCounterparty(new Counterparty {Name="Покупатель",IsSupplier=false},admin);
    var products=repo.GetProducts(); var product=products.Single(p=>p.Name=="Тест 100%"); var other=products.Single(p=>p.Name=="Другая позиция");
    var warehouses=repo.GetWarehouses(); var wa=warehouses.Single(w=>w.Name=="Склад A"); var wb=warehouses.Single(w=>w.Name=="Склад B");
    var parties=repo.GetCounterparties(); var supplier=parties.Single(p=>p.IsSupplier); var buyer=parties.Single(p=>!p.IsSupplier);
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
    repo.CreateOperation(receipt,store); repo.CreateOperation(shipment,sales); repo.CreateOperation(transfer,store);
    repo.CreateOperation(new OperationInput(OperationKind.Receipt,other.Id,2,null,wa.Id,supplier.Id,"admin receipt"),admin);
    repo.CreateOperation(new OperationInput(OperationKind.Shipment,other.Id,1,wa.Id,null,buyer.Id,"admin shipment"),admin);
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
    try { repo.CreateOperation(shipment with {Quantity=6},sales); throw new Exception("FAILED: overselling accepted"); }
    catch (InvalidOperationException) { checks++; Console.WriteLine("OK: insufficient stock rejected"); }
    try { repo.CreateOperation(transfer with {ToWarehouseId=999999},store); throw new Exception("FAILED: invalid destination accepted"); }
    catch (Exception ex) when (ex is SqlException or InvalidOperationException) { checks++; Console.WriteLine("OK: invalid destination rejected"); }
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
        try { (i%2==0 ? repo : sameStreamRepo).CreateOperation(shipment with {Quantity=1,Comment="parallel"},sales); return true; }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Недостаточно товара")) { return false; }
    })).ToArray();
    Task.WaitAll(tasks);
    Check(tasks.Count(t=>t.Result)==5,"parallel sales cannot oversell");
    Check(repo.GetStocks(new StockFilter {IncludeZero=true}).Single(s=>s.ProductId==product.Id && s.WarehouseId==wa.Id).Quantity==0,"parallel balances consistent");
    Check(repo.SyncAuditFile().State==AuditFileState.Verified,"parallel writes leave verifiable file");

    // Версия 3: реквизиты организации (только администратор), реквизиты контрагентов, многотоварные документы и ТТН.
    Check(repo.Authenticate("seller","123") is {Role:UserRole.SalesManager},"new database seeds demo sales manager seller / 123");
    Check(!repo.GetOrganization().IsFilled,"new database starts with empty organization requisites");
    var organization=new OrganizationProfile {Name="ООО «Тестовый склад»",Unp="190000001",Address="г. Минск, ул. Тестовая, 1",ReleaseAllowedBy="Директор Иванов И.И.",DefaultVatRate=20};
    Denied(()=>repo.SaveOrganization(organization,store),"storekeeper cannot change organization requisites");
    Denied(()=>repo.SaveOrganization(organization,sales),"sales manager cannot change organization requisites");
    Invalid(()=>repo.SaveOrganization(new OrganizationProfile {Name="Без УНП",Address="Минск"},admin),"organization UNP required");
    repo.SaveOrganization(organization,admin);
    Check(repo.GetOrganization().Unp=="190000001" && repo.GetAuditEntries(10000).Any(e=>e.Action=="Изменены реквизиты организации"),"admin saves organization requisites with audit");
    Denied(()=>repo.SaveCounterparty(new Counterparty {Name="Чужой покупатель",IsSupplier=false},store),"storekeeper cannot manage buyers");
    Invalid(()=>repo.SaveCounterparty(new Counterparty {Name="Плохой УНП",IsSupplier=false,Unp="123"},sales),"counterparty UNP format validated");
    buyer.Unp="191111111"; buyer.Address="г. Брест, ул. Покупателей, 5"; buyer.DeliveryAddress="г. Брест, склад покупателя"; buyer.Contract="Договор № 7 от 01.02.2026";
    repo.SaveCounterparty(buyer,sales);
    Check(repo.GetCounterparties().Single(x=>x.Id==buyer.Id).Contract=="Договор № 7 от 01.02.2026","sales manager edits buyer requisites");
    var heavy=new Product {Id=other.Id,Name=other.Name,Unit=other.Unit,PurchasePrice=other.PurchasePrice,SalePrice=other.SalePrice,VatRate=10,UnitsPerPlace=4,UnitMassKg=2.5m};
    repo.SaveProduct(heavy,store);
    Check(repo.GetProducts().Single(x=>x.Id==other.Id) is {VatRate:10m,UnitsPerPlace:4m,UnitMassKg:2.5m},"product VAT rate and cargo data persist");

    var receiptDocument=repo.CreateDocument(new DocumentInput(OperationKind.Receipt,null,wa.Id,supplier.Id,
        new[] {new DocumentLineInput(product.Id,10),new DocumentLineInput(other.Id,8) {Price=4.5m}}) {Number="ПС 123",DocumentDate=now.Date.AddDays(-1)},store);
    var received=repo.GetDocument(receiptDocument)!;
    Check(received.Lines.Count==2 && received.Lines[0].Price==product.PurchasePrice && received.Lines[1].Price==4.5m && received.DocumentDate==now.Date.AddDays(-1)
        && received.ShipperName=="Поставщик" && received.ConsigneeName==organization.Name,"multi-line receipt stores lines, prices and parties");
    Denied(()=>repo.CreateDocument(new DocumentInput(OperationKind.Shipment,wa.Id,null,buyer.Id,new[] {new DocumentLineInput(product.Id,1)}),store),"storekeeper cannot issue sales TTN");
    var auditBeforeDocument=repo.GetAuditEntries(10000).Count;
    var shipmentDocument=repo.CreateDocument(new DocumentInput(OperationKind.Shipment,wa.Id,null,buyer.Id,
        new[] {new DocumentLineInput(product.Id,3) {DiscountPercent=2,Note="розн. цена = 7,99"},new DocumentLineInput(other.Id,5)})
        {Number="АА 0000001",Vehicle="МАЗ 1234 АВ-7",Driver="Петров П.П.",WaybillNumber="15",PayerName=buyer.Name,PayerUnp=buyer.Unp},sales);
    var shipped=repo.GetDocument(shipmentDocument)!;
    var discounted=Ttn.Discounted(product.SalePrice,2);
    Check(shipped.Lines.Count==2 && shipped.Lines[0].Price==discounted && shipped.Lines[0].VatRate==20 && shipped.Lines[1].VatRate==10
        && shipped.Lines[1].CargoPlaces==2 && shipped.Lines[1].CargoMassKg==12.5m,"shipment uses discounted sale price, product VAT and cargo defaults");
    Check(shipped.ShipperName==organization.Name && shipped.ShipperUnp==organization.Unp && shipped.ConsigneeUnp=="191111111"
        && shipped.UnloadingPoint=="г. Брест, склад покупателя" && shipped.Basis==buyer.Contract && shipped.ReleaseAllowedBy==organization.ReleaseAllowedBy
        && shipped.LoadingPoint=="Минск" && shipped.HandedOverBy=="Сотрудник продаж","TTN header snapshot is filled from organization, buyer and warehouse");
    var expectedVat=Ttn.Vat(Ttn.Amount(3,discounted),20)+Ttn.Vat(Ttn.Amount(5,other.SalePrice),10);
    Check(shipped.Totals.VatAmount==expectedVat && shipped.Totals.AmountWithVat==shipped.Totals.Amount+expectedVat && shipped.Totals.CargoMassTonnes==0.0125m,"VAT is charged on top and totals are consistent");
    var documentAudit=repo.GetAuditEntries(10000).Where(e=>e.Entity=="Складской документ" && e.EntityId==shipmentDocument.ToString()).ToList();
    Check(repo.GetAuditEntries(10000).Count==auditBeforeDocument+1 && documentAudit.Count==1 && documentAudit[0].Action=="Проведена реализация"
        && documentAudit[0].Details.Contains("Склад A") && !documentAudit[0].Details.Contains(product.Name),"one audit event per document: operation, warehouses, time — without lines");
    Check(repo.GetDocuments(new OperationFilter {Search="АА 0000001"}).Single().LineCount==2,"document journal search by TTN number");
    Check(repo.GetDocuments(new OperationFilter {ProductId=other.Id,Kind=OperationKind.Shipment}).Any(x=>x.Id==shipmentDocument),"document journal filter by product");
    Check(repo.GetOperations(new OperationFilter {ProductId=product.Id,DocumentId=shipmentDocument}).Single().Price==discounted,"product history shows document lines with price");
    try { repo.CreateDocument(new DocumentInput(OperationKind.Shipment,wa.Id,null,buyer.Id,new[] {new DocumentLineInput(product.Id,1)}) {Number="АА 0000001"},sales); throw new Exception("FAILED: duplicate TTN number accepted"); }
    catch (InvalidOperationException) { checks++; Console.WriteLine("OK: duplicate TTN number rejected"); }
    var stockBeforeFailed=repo.GetStocks(new StockFilter {IncludeZero=true}).Where(x=>x.WarehouseId==wa.Id).ToDictionary(x=>x.ProductId,x=>x.Quantity);
    var documentsBeforeFailed=repo.GetDocuments(new OperationFilter()).Count;
    try { repo.CreateDocument(new DocumentInput(OperationKind.Transfer,wa.Id,wb.Id,null,new[] {new DocumentLineInput(product.Id,1),new DocumentLineInput(other.Id,1000)}),store); throw new Exception("FAILED: transfer beyond stock accepted"); }
    catch (InvalidOperationException ex) when (ex.Message.Contains(other.Name)) { checks++; Console.WriteLine("OK: insufficient line names the product"); }
    Check(repo.GetDocuments(new OperationFilter()).Count==documentsBeforeFailed
        && repo.GetStocks(new StockFilter {IncludeZero=true}).Where(x=>x.WarehouseId==wa.Id).All(x=>stockBeforeFailed[x.ProductId]==x.Quantity),"multi-line document is atomic: one short line rolls back all lines");
    var transferDocument=repo.CreateDocument(new DocumentInput(OperationKind.Transfer,wa.Id,wb.Id,null,new[] {new DocumentLineInput(product.Id,2),new DocumentLineInput(other.Id,1) {VatRate=20}}),store);
    var transferred=repo.GetDocument(transferDocument)!;
    Check(transferred.Lines.All(x=>x.VatRate is null) && transferred.Lines[0].Price==product.PurchasePrice && transferred.Basis=="Внутреннее перемещение"
        && transferred.ShipperName==organization.Name && transferred.ConsigneeName==organization.Name && transferred.UnloadingPoint=="Брест","transfer is without VAT at purchase prices between own warehouses");
    Invalid(()=>repo.CreateDocument(new DocumentInput(OperationKind.Shipment,wa.Id,null,buyer.Id,new[] {new DocumentLineInput(product.Id,1),new DocumentLineInput(product.Id,1)}),sales),"same product twice in one document rejected");
    Check(repo.GetDocumentSuggestions().Vehicles.Contains("МАЗ 1234 АВ-7"),"transport suggestions come from previous documents");

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
    var legacyDocument=legacy.GetDocuments(new OperationFilter()).Single();
    Check(legacyDocument.LineCount==1 && legacyDocument.Kind==OperationKind.Receipt && legacy.GetDocument(legacyDocument.Id)!.IsLegacy
        && legacy.GetDocument(legacyDocument.Id)!.Lines.Single().Price is null,"v3 migration turns each old operation into a one-line document");
    Check(legacy.GetProducts().Single().VatRate==20 && legacy.GetCounterparties().Single().Unp=="" && !legacy.GetOrganization().IsFilled,"v3 migration adds defaults for new requisites");
    SqlConstraint(migrationCs,"UPDATE dbo.StockOperations SET Price=0","migrated SQL rejects zero line price");
    SqlConstraint(migrationCs,"UPDATE dbo.OrganizationProfile SET DefaultVatRate=101","migrated SQL rejects VAT rate above 100 %");
    SqlConstraint(migrationCs,"INSERT dbo.Products(Name,Unit,PurchasePrice,SalePrice) VALUES (N'zero',N'шт',0,1)","migrated SQL rejects new zero price");
    SqlConstraint(migrationCs,"INSERT dbo.Warehouses(Name,Address,ManagerName,ManagerContact) VALUES (N'W',N'',N'N',N'C')","migrated SQL rejects new blank warehouse fields");
    var oldProduct=legacy.GetProducts().Single(); oldProduct.PurchasePrice=1; oldProduct.SalePrice=2; legacy.SaveProduct(oldProduct,oldAdmin);
    var oldWarehouse=legacy.GetWarehouses().Single(); oldWarehouse.Address="Минск"; oldWarehouse.ManagerName="ФИО"; oldWarehouse.ManagerContact="Телефон"; legacy.SaveWarehouse(oldWarehouse,oldAdmin);
    Check(!legacy.GetLegacyDataIssues().HasIssues,"legacy records can be corrected without data loss");
    Console.WriteLine($"SQL Server integration checks passed: {checks}.");
    return 0;
}
finally
{
    SqlConnection.ClearAllPools();
    using var c=new SqlConnection(new SqlConnectionStringBuilder(cs) {InitialCatalog="master"}.ConnectionString); c.Open();
    foreach(var name in new[] {db,migrationDb})
    {
        using var q=c.CreateCommand();
        q.CommandText=$"IF DB_ID(@db) IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END";
        q.Parameters.AddWithValue("@db",name); q.CommandTimeout=30; q.ExecuteNonQuery();
    }
    if (Directory.Exists(auditDir)) Directory.Delete(auditDir,true);
}
