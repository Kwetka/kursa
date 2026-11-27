using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Warehouse.Core.Models;
using Warehouse.Core.Services;
using WarehouseEntity = Warehouse.Core.Models.Warehouse;

namespace Warehouse.Core.Data;

public sealed partial class WarehouseRepository
{
    public long GetSuggestedInvoiceNumber()
    {
        using var c=Open(); using var q=new SqlCommand("SELECT NextNumber FROM dbo.InvoiceSequence WHERE Id=1",c);
        return Convert.ToInt64(q.ExecuteScalar());
    }

    private static PartyDetails CleanParty(PartyDetails? party, string label, bool nameRequired)
    {
        party ??= new PartyDetails();
        return new PartyDetails {
            Name=nameRequired ? Validation.Required(party.Name,$"{label}: наименование",200) : Validation.Optional(party.Name,$"{label}: наименование",200),
            Address=Validation.Optional(party.Address,$"{label}: адрес",400), Unp=Validation.Optional(party.Unp,$"{label}: УНП",30)
        };
    }

    private static PartyDetails PartyFrom(Counterparty p) => new() {Name=p.Name,Address=p.Address,Unp=p.Unp};
    private static bool SameParty(PartyDetails a,PartyDetails b) => a.Name==b.Name && a.Address==b.Address && a.Unp==b.Unp;

    private static void ValidateBatch(BatchOperationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Items is null || input.Items.Count is < 1 or > 200)
            throw new ArgumentException("Добавьте от 1 до 200 товарных позиций.");
        foreach(var item in input.Items)
        {
            if (item is null) throw new ArgumentException("Пустая товарная позиция.");
            Validation.Operation(new OperationInput(input.Kind,item.ProductId,item.Quantity,input.FromWarehouseId,input.ToWarehouseId,input.CounterpartyId,input.Comment));
            Validation.Optional(item.Note,"Примечание к товару",500);
            if (input.Kind==OperationKind.Shipment)
            {
                if (!item.PriceBeforeDiscount.HasValue) throw new ArgumentException("Для каждой позиции укажите цену реализации.");
                Validation.Price(item.PriceBeforeDiscount.Value,"Цена до скидки");
                InvoiceCalculator.Discount(item.DiscountPercent); InvoiceCalculator.VatRate(item.VatRate);
                InvoiceCalculator.OptionalCargo(item.CargoPlaces,item.MassTonnes);
            }
        }
        if (input.Kind==OperationKind.Shipment && input.Invoice is null)
            throw new ArgumentException("Для реализации обязательно оформите накладную.");
        if (input.Kind!=OperationKind.Shipment && input.Invoice is not null)
            throw new ArgumentException("Накладная оформляется только для реализации.");
        if (input.Invoice is not { } d) return;
        Validation.Optional(d.Series,"Серия накладной",30);
        if (!d.UseAutomaticNumber) Validation.Required(d.Number,"Номер накладной",50);
        if (d.Date==default) throw new ArgumentException("Укажите дату накладной.");
        if (!Enum.IsDefined(d.PriceMode)) throw new ArgumentException("Выберите порядок учёта НДС в цене.");
        if (d.PriceMode==VatPriceMode.WithoutVat && (input.Items.Any(x=>x.VatRate.HasValue) || d.TransportVatRate.HasValue))
            throw new ArgumentException("В режиме «без НДС» очистите ставки НДС у товаров и транспортных услуг.");
        if (d.PayerIsSender && d.PayerCounterpartyId.HasValue) throw new ArgumentException("Укажите только одного плательщика.");
        CleanParty(d.Recipient,"Грузополучатель",true); CleanParty(d.Payer,"Плательщик",false);
        Validation.Optional(d.Vehicle,"Автомобиль",200); Validation.Optional(d.Trailer,"Прицеп",200);
        Validation.Optional(d.WaybillNumber,"Путевой лист",100); Validation.Optional(d.Driver,"Водитель",200);
        Validation.Optional(d.BasisName,"Основание отпуска",200); Validation.Optional(d.BasisNumber,"Номер основания",100);
        Validation.Optional(d.LoadingPoint,"Пункт погрузки",400); Validation.Optional(d.UnloadingPoint,"Пункт разгрузки",400);
        if (d.TransportPrice is decimal transport) Validation.Price(transport,"Стоимость транспортных услуг");
        if (d.TransportVatRate is not null && d.TransportPrice is null) throw new ArgumentException("Укажите стоимость транспортных услуг или очистите их ставку НДС.");
        InvoiceCalculator.VatRate(d.TransportVatRate);
    }

    /// <summary>Вся группа движений, накладная и аудит фиксируются единой транзакцией SQL.</summary>
    public BatchCreationResult CreateBatch(BatchOperationInput input, AppUser actor)
    {
        ValidateBatch(input);
        var comment=Validation.Optional(input.Comment,"Комментарий",500);
        try
        {
            using var c=Open(); using var tx=c.BeginTransaction(IsolationLevel.Serializable);
            var actual=RequireActor(c,tx,actor,u=>u.CanPerform(input.Kind));
            // Встречные перемещения берут блокировки складов в одинаковом порядке.
            var warehouses=new Dictionary<int,WarehouseEntity>();
            foreach (var warehouseId in new[] {input.FromWarehouseId,input.ToWarehouseId}.Where(x=>x.HasValue).Select(x=>x!.Value).Distinct().OrderBy(x=>x))
                warehouses[warehouseId]=FindWarehouse(c,tx,warehouseId) ?? throw new InvalidOperationException("Один из складов не найден.");
            // Получатель/плательщик могут отличаться от покупателя. Блокируем все карточки в одном порядке.
            var partyIds=new List<int>();
            if (input.CounterpartyId is int partyId) partyIds.Add(partyId);
            if (actual.Role==UserRole.SalesManager && input.Invoice is { } partyDraft)
            {
                if (partyDraft.RecipientCounterpartyId is int recipientId) partyIds.Add(recipientId);
                if (partyDraft.PayerCounterpartyId is int payerId) partyIds.Add(payerId);
            }
            var parties=new Dictionary<int,Counterparty>();
            foreach(var id in partyIds.Distinct().OrderBy(x=>x))
                parties[id]=FindCounterparty(c,tx,id) ?? throw new InvalidOperationException("Один из контрагентов не найден.");
            var routeParty=input.CounterpartyId is int routeId ? parties[routeId] : null;
            if (routeParty is not null && routeParty.IsSupplier!=(input.Kind==OperationKind.Receipt))
                throw new InvalidOperationException("Для прихода нужен поставщик, для реализации — покупатель.");
            // Единый порядок захвата ресурсов предотвращает взаимную блокировку двух многотоварных операций.
            var products=new Dictionary<int,Product>();
            foreach(var id in input.Items.Select(x=>x.ProductId).Distinct().OrderBy(x=>x))
            {
                using (var l=new SqlCommand(@"DECLARE @result int, @resource nvarchar(255)=N'WarehouseCourse.Product:'+CONVERT(nvarchar(20),@product);
                    EXEC @result=sys.sp_getapplock @Resource=@resource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=15000;
                    SELECT @result;",c,tx))
                {
                    Add(l,"@product",SqlDbType.Int,id);
                    if (Convert.ToInt32(l.ExecuteScalar())<0) throw new InvalidOperationException("Товар занят другой операцией. Повторите попытку.");
                }
                products[id]=FindProduct(c,tx,id) ?? throw new InvalidOperationException("Один из товаров не найден.");
            }
            PartyDetails? sender=null;
            if (input.Kind==OperationKind.Shipment)
            {
                sender=ReadOrganization(c,tx);
                if (string.IsNullOrWhiteSpace(sender.Name) || string.IsNullOrWhiteSpace(sender.Address))
                    throw new InvalidOperationException("Администратор должен сначала заполнить наименование и адрес на вкладке «Организация».");
                if (input.Invoice?.ExpectedSender is { } expected && !SameParty(CleanParty(expected,"Грузоотправитель",false),sender))
                    throw new InvalidOperationException("Реквизиты организации изменились. Закройте и заново откройте форму реализации, чтобы проверить их.");
                if (actual.Role==UserRole.SalesManager && input.Invoice is { } sale)
                {
                    var recipient=CleanParty(sale.Recipient,"Грузополучатель",true);
                    var recipientParty=sale.RecipientCounterpartyId is int recipientId ? parties[recipientId] : routeParty;
                    if (recipientParty is null || !SameParty(recipient,PartyFrom(recipientParty)))
                        throw new InvalidOperationException("Реквизиты получателя должны соответствовать выбранной карточке контрагента. Если они изменились, обновите форму; исправляет их администратор.");
                    PartyDetails permittedPayer;
                    if (sale.PayerIsSender) permittedPayer=sender;
                    else if (sale.PayerCounterpartyId is int payerId)
                        permittedPayer=PartyFrom(parties[payerId]);
                    else permittedPayer=new PartyDetails();
                    if (!SameParty(CleanParty(sale.Payer,"Плательщик",false),permittedPayer))
                        throw new InvalidOperationException("Выберите плательщика из карточек контрагентов или организацию. Реквизиты исправляет администратор.");
                }
            }
            int batchId; DateTime occurredAt;
            using (var q=new SqlCommand(@"INSERT dbo.OperationBatches(Kind,FromWarehouseId,ToWarehouseId,CounterpartyId,Comment,UserId,
                    FromWarehouseNameSnapshot,ToWarehouseNameSnapshot,CounterpartyNameSnapshot,UserNameSnapshot)
                OUTPUT INSERTED.Id,INSERTED.OccurredAt VALUES(@kind,@from,@to,@party,@comment,@user,@fromName,@toName,@partyName,@userName)",c,tx))
            {
                Add(q,"@kind",SqlDbType.TinyInt,(byte)input.Kind); Add(q,"@from",SqlDbType.Int,input.FromWarehouseId);
                Add(q,"@to",SqlDbType.Int,input.ToWarehouseId); Add(q,"@party",SqlDbType.Int,input.CounterpartyId);
                Add(q,"@comment",SqlDbType.NVarChar,comment,500); Add(q,"@user",SqlDbType.Int,actual.Id);
                Add(q,"@fromName",SqlDbType.NVarChar,input.FromWarehouseId is int fromId ? warehouses[fromId].Name : null,200);
                Add(q,"@toName",SqlDbType.NVarChar,input.ToWarehouseId is int toId ? warehouses[toId].Name : null,200);
                Add(q,"@partyName",SqlDbType.NVarChar,routeParty?.Name,200); Add(q,"@userName",SqlDbType.NVarChar,actual.FullName,200);
                using var r=q.ExecuteReader(); r.Read(); batchId=r.GetInt32(0); occurredAt=r.GetDateTime(1);
            }
            var auditLines=new List<object>(); var invoiceLines=new List<InvoiceLine>();
            foreach(var item in input.Items)
            {
                var product=products[item.ProductId];
                var beforeFrom=ReadBalance(c,tx,item.ProductId,input.FromWarehouseId);
                var beforeTo=ReadBalance(c,tx,item.ProductId,input.ToWarehouseId);
                if (input.FromWarehouseId is int source)
                {
                    using var q=new SqlCommand(@"UPDATE dbo.StockBalances SET Quantity=Quantity-@qty
                        OUTPUT INSERTED.Quantity WHERE ProductId=@product AND WarehouseId=@warehouse AND Quantity>=@qty",c,tx);
                    Add(q,"@qty",SqlDbType.Decimal,item.Quantity); Add(q,"@product",SqlDbType.Int,item.ProductId); Add(q,"@warehouse",SqlDbType.Int,source);
                    if (q.ExecuteScalar() is null) throw new InvalidOperationException($"Недостаточно товара «{product.Name}» на исходном складе; вся операция отменена.");
                }
                if (input.ToWarehouseId is int destination)
                {
                    using var q=new SqlCommand(@"IF EXISTS(SELECT 1 FROM dbo.StockBalances WHERE ProductId=@product AND WarehouseId=@warehouse)
                        UPDATE dbo.StockBalances SET Quantity=Quantity+@qty WHERE ProductId=@product AND WarehouseId=@warehouse;
                        ELSE INSERT dbo.StockBalances(ProductId,WarehouseId,Quantity) VALUES(@product,@warehouse,@qty);",c,tx);
                    Add(q,"@qty",SqlDbType.Decimal,item.Quantity); Add(q,"@product",SqlDbType.Int,item.ProductId); Add(q,"@warehouse",SqlDbType.Int,destination); q.ExecuteNonQuery();
                }
                int lineId;
                using (var q=new SqlCommand(@"INSERT dbo.StockOperations(OccurredAt,Kind,ProductId,Quantity,FromWarehouseId,ToWarehouseId,CounterpartyId,Comment,UserId,BatchId,ProductNameSnapshot,UnitSnapshot)
                    OUTPUT INSERTED.Id VALUES(@time,@kind,@product,@qty,@from,@to,@party,@comment,@user,@batch,@productName,@unit)",c,tx))
                {
                    Add(q,"@time",SqlDbType.DateTime2,occurredAt); Add(q,"@kind",SqlDbType.TinyInt,(byte)input.Kind);
                    Add(q,"@product",SqlDbType.Int,item.ProductId); Add(q,"@qty",SqlDbType.Decimal,item.Quantity);
                    Add(q,"@from",SqlDbType.Int,input.FromWarehouseId); Add(q,"@to",SqlDbType.Int,input.ToWarehouseId);
                    Add(q,"@party",SqlDbType.Int,input.CounterpartyId); Add(q,"@comment",SqlDbType.NVarChar,comment,500);
                    Add(q,"@user",SqlDbType.Int,actual.Id); Add(q,"@batch",SqlDbType.Int,batchId);
                    Add(q,"@productName",SqlDbType.NVarChar,product.Name,200); Add(q,"@unit",SqlDbType.NVarChar,product.Unit,30); lineId=Convert.ToInt32(q.ExecuteScalar());
                }
                auditLines.Add(new {Id=lineId,ProductId=item.ProductId,ProductName=product.Name,item.Quantity,
                    SourceBefore=beforeFrom,SourceAfter=ReadBalance(c,tx,item.ProductId,input.FromWarehouseId),
                    DestinationBefore=beforeTo,DestinationAfter=ReadBalance(c,tx,item.ProductId,input.ToWarehouseId)});
                if (input.Invoice is { } draft) invoiceLines.Add(InvoiceCalculator.Product(item,product,draft.PriceMode));
            }
            InvoiceDocument? document=null;
            if (input.Invoice is { } d)
            {
                var series=Validation.Optional(d.Series,"Серия накладной",30);
                var number=d.UseAutomaticNumber ? AllocateNumber(c,tx,series) : Validation.Required(d.Number,"Номер накладной",50);
                if (d.TransportPrice is decimal price) invoiceLines.Add(InvoiceCalculator.Transport(price,d.TransportVatRate,d.PriceMode));
                document=new InvoiceDocument {BatchId=batchId, Series=series, Number=number, Date=d.Date.Date, PriceMode=d.PriceMode,
                    Sender=sender!, Recipient=CleanParty(d.Recipient,"Грузополучатель",true), Payer=CleanParty(d.Payer,"Плательщик",false),
                    Vehicle=Validation.Optional(d.Vehicle,"Автомобиль",200), Trailer=Validation.Optional(d.Trailer,"Прицеп",200),
                    WaybillNumber=Validation.Optional(d.WaybillNumber,"Путевой лист",100), Driver=Validation.Optional(d.Driver,"Водитель",200),
                    BasisName=Validation.Optional(d.BasisName,"Основание отпуска",200), BasisNumber=Validation.Optional(d.BasisNumber,"Номер основания",100),
                    BasisDate=d.BasisDate?.Date, LoadingPoint=Validation.Optional(d.LoadingPoint,"Пункт погрузки",400),
                    UnloadingPoint=Validation.Optional(d.UnloadingPoint,"Пункт разгрузки",400), Lines=invoiceLines};
                using (var q=new SqlCommand(@"INSERT dbo.Invoices(BatchId,Series,Number,DocumentDate,Payload)
                    OUTPUT INSERTED.Id VALUES(@batch,@series,@number,@date,@payload)",c,tx))
                {
                    Add(q,"@batch",SqlDbType.Int,batchId); Add(q,"@series",SqlDbType.NVarChar,series,30); Add(q,"@number",SqlDbType.NVarChar,number,50);
                    Add(q,"@date",SqlDbType.Date,document.Date); Add(q,"@payload",SqlDbType.NVarChar,JsonSerializer.Serialize(document,AuditJson),-1);
                    document.Id=Convert.ToInt32(q.ExecuteScalar());
                }
                // ID документа включён в снимок, чтобы его можно было выгрузить независимо от текущих справочников.
                using (var q=new SqlCommand("UPDATE dbo.Invoices SET Payload=@payload WHERE Id=@id",c,tx))
                { Add(q,"@payload",SqlDbType.NVarChar,JsonSerializer.Serialize(document,AuditJson),-1); Add(q,"@id",SqlDbType.Int,document.Id); q.ExecuteNonQuery(); }
            }
            var action=input.Kind switch {OperationKind.Receipt=>"Проведён приход",OperationKind.Shipment=>"Проведена реализация",_=>"Проведено перемещение"};
            WriteAudit(c,tx,actual,action,"Групповая складская операция",batchId.ToString(CultureInfo.InvariantCulture),
                new {input.Kind,input.FromWarehouseId,input.ToWarehouseId,input.CounterpartyId,Comment=comment,Items=auditLines,Invoice=document});
            tx.Commit(); SyncAuditFile();
            return new BatchCreationResult(batchId,document);
        }
        catch(SqlException ex) when (ex.Number is 2601 or 2627 && ex.Message.Contains("Invoices",StringComparison.OrdinalIgnoreCase))
        { throw new InvalidOperationException("Накладная с такой серией и номером уже существует. Проверьте номер; операция не проведена.",ex); }
    }

    private static string AllocateNumber(SqlConnection c, SqlTransaction tx, string series)
    {
        long number;
        using (var q=new SqlCommand("SELECT NextNumber FROM dbo.InvoiceSequence WITH(UPDLOCK,HOLDLOCK) WHERE Id=1",c,tx)) number=Convert.ToInt64(q.ExecuteScalar());
        while(true)
        {
            var text=number.ToString(CultureInfo.InvariantCulture);
            using var q=new SqlCommand("SELECT COUNT(*) FROM dbo.Invoices WITH(UPDLOCK,HOLDLOCK) WHERE Series=@series AND Number=@number",c,tx);
            Add(q,"@series",SqlDbType.NVarChar,series,30); Add(q,"@number",SqlDbType.NVarChar,text,50);
            if (Convert.ToInt32(q.ExecuteScalar())==0)
            {
                if (number==long.MaxValue) throw new InvalidOperationException("Исчерпаны внутренние номера накладных.");
                using var update=new SqlCommand("UPDATE dbo.InvoiceSequence SET NextNumber=@next WHERE Id=1",c,tx);
                Add(update,"@next",SqlDbType.BigInt,number+1); update.ExecuteNonQuery(); return text;
            }
            if (number==long.MaxValue) throw new InvalidOperationException("Исчерпаны внутренние номера накладных.");
            number++;
        }
    }

    public InvoiceDocument? GetInvoice(int batchId)
    {
        using var c=Open(); using var q=new SqlCommand("SELECT Id,Payload FROM dbo.Invoices WHERE BatchId=@id",c);
        Add(q,"@id",SqlDbType.Int,batchId); using var r=q.ExecuteReader();
        if (!r.Read()) return null;
        var doc=JsonSerializer.Deserialize<InvoiceDocument>(r.GetString(1)) ?? throw new InvalidOperationException("Не удалось прочитать сохранённую накладную.");
        doc.Id=r.GetInt32(0); return doc;
    }

    public List<OperationBatchRow> GetOperationBatches(OperationFilter filter)
    {
        Validation.Interval(filter.FromInclusive,filter.ToExclusive);
        using var c=Open(); using var q=c.CreateCommand();
        q.CommandText=@"WITH groups AS (
            SELECT b.Id AS BatchId,CAST(NULL AS int) AS LegacyId,b.OccurredAt,b.Kind,b.FromWarehouseId,b.ToWarehouseId,b.CounterpartyId,b.Comment,b.UserId
              FROM dbo.OperationBatches b
            UNION ALL
            SELECT CAST(NULL AS int),o.Id,o.OccurredAt,o.Kind,o.FromWarehouseId,o.ToWarehouseId,o.CounterpartyId,o.Comment,o.UserId
              FROM dbo.StockOperations o WHERE o.BatchId IS NULL)
          SELECT g.BatchId,g.LegacyId,g.OccurredAt,g.Kind,
            CASE WHEN g.BatchId IS NULL THEN 1 ELSE (SELECT COUNT(*) FROM dbo.StockOperations x WHERE x.BatchId=g.BatchId) END,
            COALESCE(bs.FromWarehouseNameSnapshot,wf.Name,N'—'),COALESCE(bs.ToWarehouseNameSnapshot,wt.Name,N'—'),
            COALESCE(bs.CounterpartyNameSnapshot,cp.Name,N'—'),g.Comment,COALESCE(bs.UserNameSnapshot,u.FullName),i.Id,
            CASE WHEN i.Id IS NULL THEN N'—' ELSE CASE WHEN i.Series=N'' THEN N'' ELSE i.Series+N' ' END+N'№'+i.Number END
          FROM groups g LEFT JOIN dbo.OperationBatches bs ON bs.Id=g.BatchId LEFT JOIN dbo.Warehouses wf ON wf.Id=g.FromWarehouseId LEFT JOIN dbo.Warehouses wt ON wt.Id=g.ToWarehouseId
            LEFT JOIN dbo.Counterparties cp ON cp.Id=g.CounterpartyId JOIN dbo.WarehouseUsers u ON u.Id=g.UserId LEFT JOIN dbo.Invoices i ON i.BatchId=g.BatchId
          WHERE (@from IS NULL OR g.OccurredAt>=@from) AND (@to IS NULL OR g.OccurredAt<@to)
            AND (@warehouse IS NULL OR g.FromWarehouseId=@warehouse OR g.ToWarehouseId=@warehouse)
            AND (@party IS NULL OR g.CounterpartyId=@party) AND (@kind IS NULL OR g.Kind=@kind)
            AND (@product IS NULL OR EXISTS(SELECT 1 FROM dbo.StockOperations x WHERE
                ((g.BatchId IS NOT NULL AND x.BatchId=g.BatchId) OR x.Id=g.LegacyId) AND x.ProductId=@product))
            AND (g.Comment LIKE @pattern ESCAPE N'~' OR wf.Name LIKE @pattern ESCAPE N'~' OR wt.Name LIKE @pattern ESCAPE N'~'
                OR cp.Name LIKE @pattern ESCAPE N'~' OR u.FullName LIKE @pattern ESCAPE N'~' OR i.Number LIKE @pattern ESCAPE N'~' OR i.Series LIKE @pattern ESCAPE N'~'
                OR bs.FromWarehouseNameSnapshot LIKE @pattern ESCAPE N'~' OR bs.ToWarehouseNameSnapshot LIKE @pattern ESCAPE N'~'
                OR bs.CounterpartyNameSnapshot LIKE @pattern ESCAPE N'~' OR bs.UserNameSnapshot LIKE @pattern ESCAPE N'~'
                OR CONVERT(nvarchar(20),COALESCE(g.BatchId,g.LegacyId)) LIKE @pattern ESCAPE N'~'
                OR EXISTS(SELECT 1 FROM dbo.StockOperations x JOIN dbo.Products p ON p.Id=x.ProductId
                    WHERE ((g.BatchId IS NOT NULL AND x.BatchId=g.BatchId) OR x.Id=g.LegacyId) AND (p.Name LIKE @pattern ESCAPE N'~' OR x.ProductNameSnapshot LIKE @pattern ESCAPE N'~')))
          ORDER BY g.OccurredAt DESC,COALESCE(g.BatchId,g.LegacyId) DESC";
        Add(q,"@from",SqlDbType.DateTime2,filter.FromInclusive); Add(q,"@to",SqlDbType.DateTime2,filter.ToExclusive);
        Add(q,"@warehouse",SqlDbType.Int,filter.WarehouseId); Add(q,"@party",SqlDbType.Int,filter.CounterpartyId);
        Add(q,"@kind",SqlDbType.TinyInt,filter.Kind.HasValue ? (byte)filter.Kind.Value : null); Add(q,"@product",SqlDbType.Int,filter.ProductId);
        Add(q,"@pattern",SqlDbType.NVarChar,Pattern(filter.Search),802);
        var result=new List<OperationBatchRow>(); using var r=q.ExecuteReader();
        while(r.Read()) result.Add(new OperationBatchRow {BatchId=r.IsDBNull(0)?null:r.GetInt32(0),LegacyOperationId=r.IsDBNull(1)?null:r.GetInt32(1),
            OccurredAt=r.GetDateTime(2),Kind=(OperationKind)r.GetByte(3),ItemCount=r.GetInt32(4),FromWarehouse=r.GetString(5),ToWarehouse=r.GetString(6),
            CounterpartyName=r.GetString(7),Comment=r.GetString(8),UserName=r.GetString(9),InvoiceId=r.IsDBNull(10)?null:r.GetInt32(10),InvoiceLabel=r.GetString(11)});
        return result;
    }

    public List<OperationRow> GetOperationItems(int? batchId, int? legacyOperationId)
    {
        if (batchId.HasValue==legacyOperationId.HasValue) throw new ArgumentException("Выберите одну операцию.");
        using var c=Open(); using var q=c.CreateCommand();
        q.CommandText=@"SELECT o.Id,o.OccurredAt,o.Kind,COALESCE(o.ProductNameSnapshot,p.Name),o.Quantity,COALESCE(o.UnitSnapshot,p.Unit),
            COALESCE(bs.FromWarehouseNameSnapshot,wf.Name,N'—'),COALESCE(bs.ToWarehouseNameSnapshot,wt.Name,N'—'),
            COALESCE(bs.CounterpartyNameSnapshot,cp.Name,N'—'),o.Comment,COALESCE(bs.UserNameSnapshot,u.FullName),o.BatchId
            FROM dbo.StockOperations o JOIN dbo.Products p ON p.Id=o.ProductId LEFT JOIN dbo.OperationBatches bs ON bs.Id=o.BatchId
            LEFT JOIN dbo.Warehouses wf ON wf.Id=o.FromWarehouseId LEFT JOIN dbo.Warehouses wt ON wt.Id=o.ToWarehouseId
            LEFT JOIN dbo.Counterparties cp ON cp.Id=o.CounterpartyId JOIN dbo.WarehouseUsers u ON u.Id=o.UserId
            WHERE (@batch IS NOT NULL AND o.BatchId=@batch) OR (@legacy IS NOT NULL AND o.Id=@legacy AND o.BatchId IS NULL) ORDER BY o.Id";
        Add(q,"@batch",SqlDbType.Int,batchId); Add(q,"@legacy",SqlDbType.Int,legacyOperationId);
        var result=new List<OperationRow>(); using var r=q.ExecuteReader();
        while(r.Read()) result.Add(ReadOperationRow(r)); return result;
    }
}
