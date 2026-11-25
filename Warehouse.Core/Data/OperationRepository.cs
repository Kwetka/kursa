using System.Data;
using Microsoft.Data.SqlClient;
using Warehouse.Core.Models;
using Warehouse.Core.Services;
using WarehouseEntity = Warehouse.Core.Models.Warehouse;

namespace Warehouse.Core.Data;

public sealed partial class WarehouseRepository
{
    /// <summary>
    /// Однотоварная операция прежнего формата. Проводится как документ из одной позиции;
    /// возвращает идентификатор позиции (движения).
    /// </summary>
    public int CreateOperation(OperationInput operation, AppUser actor)
    {
        Validation.Operation(operation);
        var document = new DocumentInput(operation.Kind, operation.FromWarehouseId, operation.ToWarehouseId, operation.CounterpartyId,
            new[] { new DocumentLineInput(operation.ProductId, operation.Quantity) }) { Comment = operation.Comment };
        return CreateDocumentCore(document, actor).LineIds[0];
    }

    /// <summary>
    /// Проводит документ с несколькими товарами. Шапка, все позиции, изменение остатков и одна запись журнала
    /// выполняются в одной транзакции: либо проводится весь документ, либо ничего.
    /// </summary>
    public int CreateDocument(DocumentInput document, AppUser actor) => CreateDocumentCore(document, actor).DocumentId;

    private (int DocumentId, List<int> LineIds) CreateDocumentCore(DocumentInput input, AppUser actor)
    {
        var document = Validation.Document(input);
        using var c = Open();
        using var tx = c.BeginTransaction(IsolationLevel.Serializable);
        var actual = RequireActor(c, tx, actor, u => u.CanPerform(document.Kind));

        // Порядок блокировок одинаков для всех транзакций: номер документа, затем товары по возрастанию Id.
        if (document.Number.Length > 0)
        {
            AppLock(c, tx, "WarehouseCourse.DocumentNumber", "Номер документа проверяется другой операцией. Повторите попытку.");
            using var q = new SqlCommand(@"SELECT TOP (1) Id FROM dbo.StockDocuments
                WHERE Number=@number AND (Kind IN (1,2) AND @kind IN (1,2) OR Kind=0 AND @kind=0 AND CounterpartyId=@party)", c, tx);
            Add(q, "@number", SqlDbType.NVarChar, document.Number, 30); Add(q, "@kind", SqlDbType.TinyInt, (byte)document.Kind);
            Add(q, "@party", SqlDbType.Int, document.CounterpartyId);
            if (q.ExecuteScalar() is not null)
                throw new InvalidOperationException(document.Kind == OperationKind.Receipt
                    ? $"Накладная поставщика № {document.Number} уже оприходована."
                    : $"ТТН № {document.Number} уже выписана. Укажите другой номер.");
        }
        foreach (var productId in document.Lines.Select(x => x.ProductId).Order())
            AppLock(c, tx, "WarehouseCourse.Product:" + productId, "Товар занят другой операцией. Повторите попытку.");

        Counterparty? party = null;
        if (document.CounterpartyId is int partyId)
        {
            party = FindCounterparty(c, tx, partyId);
            if (party is null || party.IsSupplier != (document.Kind == OperationKind.Receipt))
                throw new InvalidOperationException("Для прихода нужен поставщик, для расхода — покупатель.");
        }
        // Склады читаются разделяемой блокировкой (Serializable удерживает её до конца транзакции):
        // документы по одному складу не блокируют друг друга, а изменение склада дождётся проведения.
        var source = document.FromWarehouseId is int fromId ? ReadWarehouseShared(c, tx, fromId) ?? throw new InvalidOperationException("Склад-отправитель не найден.") : null;
        var destination = document.ToWarehouseId is int toId ? ReadWarehouseShared(c, tx, toId) ?? throw new InvalidOperationException("Склад-получатель не найден.") : null;
        var organization = ReadOrganization(c, tx, false);

        // Значения позиций по умолчанию берутся из карточки товара (на сервере, а не из окна программы).
        var lines = new List<(DocumentLineInput Line, Product Product)>();
        foreach (var line in document.Lines)
        {
            var product = FindProduct(c, tx, line.ProductId) ?? throw new InvalidOperationException("Товар не найден. Обновите список товаров.");
            var basePrice = document.Kind == OperationKind.Shipment ? Ttn.Discounted(product.SalePrice, line.DiscountPercent) : product.PurchasePrice;
            var price = line.Price ?? (basePrice > 0 ? basePrice : null);
            lines.Add((line with
            {
                Price = price,
                VatRate = document.Kind == OperationKind.Transfer ? null : line.VatRate ?? product.VatRate,
                CargoPlaces = line.CargoPlaces ?? Ttn.Places(line.Quantity, product.UnitsPerPlace),
                CargoMassKg = line.CargoMassKg ?? Ttn.MassKg(line.Quantity, product.UnitMassKg)
            }, product));
        }

        // Реквизиты сторон фиксируются снимком: грузоотправитель и грузополучатель зависят от типа документа.
        var shipper = document.Kind == OperationKind.Receipt ? (party!.Name, party.Unp, party.Address) : (organization.Name, organization.Unp, organization.Address);
        var consignee = document.Kind == OperationKind.Shipment ? (party!.Name, party.Unp, party.Address) : (organization.Name, organization.Unp, organization.Address);
        var loading = Fallback(document.LoadingPoint, document.Kind == OperationKind.Receipt ? party!.Address : source!.Address);
        var unloading = Fallback(document.UnloadingPoint, document.Kind == OperationKind.Shipment ? Fallback(party!.DeliveryAddress, party.Address) : destination!.Address);
        var basis = Fallback(document.Basis, document.Kind == OperationKind.Transfer ? "Внутреннее перемещение" : party!.Contract);
        var release = Fallback(document.ReleaseAllowedBy, document.Kind == OperationKind.Receipt ? "" : organization.ReleaseAllowedBy);
        var handed = Fallback(document.HandedOverBy, document.Kind == OperationKind.Receipt ? "" : actual.FullName);

        int documentId; DateTime occurredAt;
        using (var q = new SqlCommand(@"INSERT dbo.StockDocuments(Kind,Number,DocumentDate,FromWarehouseId,ToWarehouseId,CounterpartyId,UserId,Comment,
                ShipperName,ShipperUnp,ShipperAddress,ConsigneeName,ConsigneeUnp,ConsigneeAddress,PayerName,PayerUnp,Vehicle,Trailer,Driver,
                WaybillNumber,Basis,LoadingPoint,UnloadingPoint,ReleaseAllowedBy,HandedOverBy)
            OUTPUT INSERTED.Id, INSERTED.OccurredAt
            VALUES (@kind,@number,@date,@from,@to,@party,@user,@comment,@shipperName,@shipperUnp,@shipperAddress,
                @consigneeName,@consigneeUnp,@consigneeAddress,@payerName,@payerUnp,@vehicle,@trailer,@driver,
                @waybill,@basis,@loading,@unloading,@release,@handed)", c, tx))
        {
            Add(q, "@kind", SqlDbType.TinyInt, (byte)document.Kind); Add(q, "@number", SqlDbType.NVarChar, document.Number, 30);
            Add(q, "@date", SqlDbType.Date, document.DocumentDate); Add(q, "@from", SqlDbType.Int, document.FromWarehouseId);
            Add(q, "@to", SqlDbType.Int, document.ToWarehouseId); Add(q, "@party", SqlDbType.Int, document.CounterpartyId);
            Add(q, "@user", SqlDbType.Int, actual.Id); Add(q, "@comment", SqlDbType.NVarChar, document.Comment, 500);
            Add(q, "@shipperName", SqlDbType.NVarChar, Cut(shipper.Item1, 300), 300); Add(q, "@shipperUnp", SqlDbType.NVarChar, shipper.Item2, 9);
            Add(q, "@shipperAddress", SqlDbType.NVarChar, Cut(shipper.Item3, 400), 400);
            Add(q, "@consigneeName", SqlDbType.NVarChar, Cut(consignee.Item1, 300), 300); Add(q, "@consigneeUnp", SqlDbType.NVarChar, consignee.Item2, 9);
            Add(q, "@consigneeAddress", SqlDbType.NVarChar, Cut(consignee.Item3, 400), 400);
            Add(q, "@payerName", SqlDbType.NVarChar, document.PayerName, 300); Add(q, "@payerUnp", SqlDbType.NVarChar, document.PayerUnp, 9);
            Add(q, "@vehicle", SqlDbType.NVarChar, document.Vehicle, 100); Add(q, "@trailer", SqlDbType.NVarChar, document.Trailer, 100);
            Add(q, "@driver", SqlDbType.NVarChar, document.Driver, 200); Add(q, "@waybill", SqlDbType.NVarChar, document.WaybillNumber, 50);
            Add(q, "@basis", SqlDbType.NVarChar, Cut(basis, 300), 300); Add(q, "@loading", SqlDbType.NVarChar, Cut(loading, 400), 400);
            Add(q, "@unloading", SqlDbType.NVarChar, Cut(unloading, 400), 400); Add(q, "@release", SqlDbType.NVarChar, Cut(release, 200), 200);
            Add(q, "@handed", SqlDbType.NVarChar, Cut(handed, 200), 200);
            using var r = q.ExecuteReader();
            r.Read(); documentId = r.GetInt32(0); occurredAt = r.GetDateTime(1);
        }

        var lineIds = new List<int>();
        foreach (var (line, product) in lines)
        {
            if (document.FromWarehouseId is int from)
            {
                using var q = new SqlCommand(@"UPDATE dbo.StockBalances SET Quantity = Quantity - @qty
                    OUTPUT INSERTED.Quantity WHERE ProductId=@product AND WarehouseId=@warehouse AND Quantity >= @qty", c, tx);
                Add(q, "@qty", SqlDbType.Decimal, line.Quantity); Add(q, "@product", SqlDbType.Int, line.ProductId); Add(q, "@warehouse", SqlDbType.Int, from);
                if (q.ExecuteScalar() is null)
                {
                    var available = ReadBalance(c, tx, line.ProductId, from) ?? 0m;
                    throw new InvalidOperationException($"Недостаточно товара на исходном складе: «{product.Name}» — в наличии {available:0.###} {product.Unit}, требуется {line.Quantity:0.###}.");
                }
            }
            if (document.ToWarehouseId is int to)
            {
                using var q = new SqlCommand(@"IF EXISTS (SELECT 1 FROM dbo.StockBalances WHERE ProductId=@product AND WarehouseId=@warehouse)
                    UPDATE dbo.StockBalances SET Quantity=Quantity+@qty WHERE ProductId=@product AND WarehouseId=@warehouse;
                    ELSE INSERT dbo.StockBalances(ProductId,WarehouseId,Quantity) VALUES (@product,@warehouse,@qty);", c, tx);
                Add(q, "@qty", SqlDbType.Decimal, line.Quantity); Add(q, "@product", SqlDbType.Int, line.ProductId); Add(q, "@warehouse", SqlDbType.Int, to);
                q.ExecuteNonQuery();
            }
            using (var q = new SqlCommand(@"INSERT dbo.StockOperations(DocumentId,OccurredAt,Kind,ProductId,Quantity,FromWarehouseId,ToWarehouseId,CounterpartyId,
                    Comment,UserId,Price,DiscountPercent,VatRate,CargoPlaces,CargoMassKg)
                OUTPUT INSERTED.Id VALUES (@document,@at,@kind,@product,@qty,@from,@to,@party,@comment,@user,@price,@discount,@vat,@places,@mass)", c, tx))
            {
                Add(q, "@document", SqlDbType.Int, documentId); Add(q, "@at", SqlDbType.DateTime2, occurredAt);
                Add(q, "@kind", SqlDbType.TinyInt, (byte)document.Kind); Add(q, "@product", SqlDbType.Int, line.ProductId);
                Add(q, "@qty", SqlDbType.Decimal, line.Quantity); Add(q, "@from", SqlDbType.Int, document.FromWarehouseId);
                Add(q, "@to", SqlDbType.Int, document.ToWarehouseId); Add(q, "@party", SqlDbType.Int, document.CounterpartyId);
                Add(q, "@comment", SqlDbType.NVarChar, line.Note, 500); Add(q, "@user", SqlDbType.Int, actual.Id);
                Add(q, "@price", SqlDbType.Decimal, line.Price); Add(q, "@discount", SqlDbType.Decimal, line.DiscountPercent);
                Add(q, "@vat", SqlDbType.Decimal, line.VatRate); Add(q, "@places", SqlDbType.Decimal, line.CargoPlaces);
                Add(q, "@mass", SqlDbType.Decimal, line.CargoMassKg);
                lineIds.Add(Convert.ToInt32(q.ExecuteScalar()));
            }
        }

        // В журнал пишется только сама операция: тип, номер, склады и время. Состав — в окне позиций документа.
        var action = document.Kind switch { OperationKind.Receipt => "Проведён приход", OperationKind.Shipment => "Проведена реализация", _ => "Проведено перемещение" };
        WriteAudit(c, tx, actual, action, "Складской документ", documentId.ToString(), new
        {
            document.Kind,
            Number = Documents.NumberDisplay(document.Number, documentId),
            FromWarehouse = source?.Name,
            ToWarehouse = destination?.Name,
            OccurredAt = occurredAt
        });
        tx.Commit();
        SyncAuditFile();
        return (documentId, lineIds);
    }

    private static WarehouseEntity? ReadWarehouseShared(SqlConnection c, SqlTransaction tx, int id)
    {
        using var q = new SqlCommand("SELECT Id,Name,Address,ManagerName,ManagerContact FROM dbo.Warehouses WHERE Id=@id", c, tx);
        Add(q, "@id", SqlDbType.Int, id);
        using var r = q.ExecuteReader();
        return r.Read() ? ReadWarehouse(r) : null;
    }

    private static string Fallback(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? (fallback ?? "").Trim() : value;
    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];

    private static void AppLock(SqlConnection c, SqlTransaction tx, string resource, string busyMessage)
    {
        using var q = new SqlCommand(@"DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource=@resource, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=15000;
            SELECT @result;", c, tx);
        Add(q, "@resource", SqlDbType.NVarChar, resource, 255);
        if (Convert.ToInt32(q.ExecuteScalar()) < 0) throw new InvalidOperationException(busyMessage);
    }

    private static decimal? ReadBalance(SqlConnection c, SqlTransaction tx, int productId, int? warehouseId)
    {
        if (warehouseId is null) return null;
        using var q = new SqlCommand("SELECT Quantity FROM dbo.StockBalances WHERE ProductId=@product AND WarehouseId=@warehouse", c, tx);
        Add(q, "@product", SqlDbType.Int, productId); Add(q, "@warehouse", SqlDbType.Int, warehouseId);
        var value=q.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToDecimal(value);
    }

    /// <summary>Позиции документов (движения товаров). С фильтром по товару — история операций по товару.</summary>
    public List<OperationRow> GetOperations(OperationFilter filter)
    {
        Validation.Interval(filter.FromInclusive, filter.ToExclusive);
        var result = new List<OperationRow>();
        using var c = Open(); using var q = c.CreateCommand();
        q.CommandText = @"SELECT o.Id,o.OccurredAt,o.Kind,p.Name,o.Quantity,p.Unit,
            COALESCE(wf.Name,N'—'),COALESCE(wt.Name,N'—'),COALESCE(cp.Name,N'—'),
            CASE WHEN o.Comment<>N'' THEN o.Comment ELSE d.Comment END,u.FullName,o.DocumentId,d.Number,o.Price
            FROM dbo.StockOperations o JOIN dbo.StockDocuments d ON d.Id=o.DocumentId JOIN dbo.Products p ON p.Id=o.ProductId
            LEFT JOIN dbo.Warehouses wf ON wf.Id=o.FromWarehouseId LEFT JOIN dbo.Warehouses wt ON wt.Id=o.ToWarehouseId
            LEFT JOIN dbo.Counterparties cp ON cp.Id=o.CounterpartyId JOIN dbo.WarehouseUsers u ON u.Id=o.UserId
            WHERE (@from IS NULL OR o.OccurredAt>=@from) AND (@to IS NULL OR o.OccurredAt<@to)
              AND (@product IS NULL OR o.ProductId=@product) AND (@warehouse IS NULL OR o.FromWarehouseId=@warehouse OR o.ToWarehouseId=@warehouse)
              AND (@party IS NULL OR o.CounterpartyId=@party) AND (@kind IS NULL OR o.Kind=@kind) AND (@document IS NULL OR o.DocumentId=@document)
              AND (p.Name LIKE @pattern ESCAPE N'~' OR wf.Name LIKE @pattern ESCAPE N'~' OR wt.Name LIKE @pattern ESCAPE N'~'
                   OR cp.Name LIKE @pattern ESCAPE N'~' OR o.Comment LIKE @pattern ESCAPE N'~' OR d.Comment LIKE @pattern ESCAPE N'~'
                   OR u.FullName LIKE @pattern ESCAPE N'~' OR d.Number LIKE @pattern ESCAPE N'~'
                   OR CONVERT(nvarchar(20),o.Id) LIKE @pattern ESCAPE N'~')
            ORDER BY o.OccurredAt DESC,o.Id DESC";
        AddOperationFilter(q, filter);
        using var r=q.ExecuteReader();
        while(r.Read()) result.Add(new OperationRow { Id=r.GetInt32(0), OccurredAt=r.GetDateTime(1), Kind=(OperationKind)r.GetByte(2),
            ProductName=r.GetString(3),Quantity=r.GetDecimal(4),Unit=r.GetString(5),FromWarehouse=r.GetString(6),ToWarehouse=r.GetString(7),
            CounterpartyName=r.GetString(8),Comment=r.GetString(9),UserName=r.GetString(10),DocumentId=r.GetInt32(11),DocumentNumber=r.GetString(12),
            Price=r.IsDBNull(13) ? null : r.GetDecimal(13) });
        return result;
    }

    private static void AddOperationFilter(SqlCommand q, OperationFilter filter)
    {
        Add(q,"@from",SqlDbType.DateTime2,filter.FromInclusive); Add(q,"@to",SqlDbType.DateTime2,filter.ToExclusive);
        Add(q,"@product",SqlDbType.Int,filter.ProductId); Add(q,"@warehouse",SqlDbType.Int,filter.WarehouseId); Add(q,"@party",SqlDbType.Int,filter.CounterpartyId);
        Add(q,"@kind",SqlDbType.TinyInt,filter.Kind.HasValue ? (byte)filter.Kind.Value : null); Add(q,"@document",SqlDbType.Int,filter.DocumentId);
        Add(q,"@pattern",SqlDbType.NVarChar,Pattern(filter.Search), 802);
    }

    /// <summary>Журнал документов: операция, номер, склады, время — без перечня товаров.</summary>
    public List<DocumentRow> GetDocuments(OperationFilter filter)
    {
        Validation.Interval(filter.FromInclusive, filter.ToExclusive);
        var result = new List<DocumentRow>();
        using var c = Open(); using var q = c.CreateCommand();
        q.CommandText = @"SELECT d.Id,d.Kind,d.Number,d.OccurredAt,COALESCE(wf.Name,N'—'),COALESCE(wt.Name,N'—'),COALESCE(cp.Name,N'—'),u.FullName,
                (SELECT COUNT(*) FROM dbo.StockOperations x WHERE x.DocumentId=d.Id),d.Comment
            FROM dbo.StockDocuments d
            LEFT JOIN dbo.Warehouses wf ON wf.Id=d.FromWarehouseId LEFT JOIN dbo.Warehouses wt ON wt.Id=d.ToWarehouseId
            LEFT JOIN dbo.Counterparties cp ON cp.Id=d.CounterpartyId JOIN dbo.WarehouseUsers u ON u.Id=d.UserId
            WHERE (@from IS NULL OR d.OccurredAt>=@from) AND (@to IS NULL OR d.OccurredAt<@to)
              AND (@product IS NULL OR EXISTS (SELECT 1 FROM dbo.StockOperations x WHERE x.DocumentId=d.Id AND x.ProductId=@product))
              AND (@warehouse IS NULL OR d.FromWarehouseId=@warehouse OR d.ToWarehouseId=@warehouse)
              AND (@party IS NULL OR d.CounterpartyId=@party) AND (@kind IS NULL OR d.Kind=@kind) AND (@document IS NULL OR d.Id=@document)
              AND (wf.Name LIKE @pattern ESCAPE N'~' OR wt.Name LIKE @pattern ESCAPE N'~' OR cp.Name LIKE @pattern ESCAPE N'~'
                   OR d.Comment LIKE @pattern ESCAPE N'~' OR u.FullName LIKE @pattern ESCAPE N'~' OR d.Number LIKE @pattern ESCAPE N'~'
                   OR CONVERT(nvarchar(20),d.Id) LIKE @pattern ESCAPE N'~'
                   OR EXISTS (SELECT 1 FROM dbo.StockOperations x JOIN dbo.Products p ON p.Id=x.ProductId
                              WHERE x.DocumentId=d.Id AND (p.Name LIKE @pattern ESCAPE N'~' OR x.Comment LIKE @pattern ESCAPE N'~')))
            ORDER BY d.OccurredAt DESC,d.Id DESC";
        AddOperationFilter(q, filter);
        using var r=q.ExecuteReader();
        while(r.Read()) result.Add(new DocumentRow { Id=r.GetInt32(0), Kind=(OperationKind)r.GetByte(1), Number=r.GetString(2), OccurredAt=r.GetDateTime(3),
            FromWarehouse=r.GetString(4), ToWarehouse=r.GetString(5), CounterpartyName=r.GetString(6), UserName=r.GetString(7),
            LineCount=r.GetInt32(8), Comment=r.GetString(9) });
        return result;
    }

    /// <summary>Документ целиком: шапка ТТН и позиции с расчётом НДС.</summary>
    public StockDocument? GetDocument(int id)
    {
        using var c = Open();
        StockDocument document;
        using (var q = c.CreateCommand())
        {
            q.CommandText = @"SELECT d.Id,d.Kind,d.Number,d.OccurredAt,d.DocumentDate,d.FromWarehouseId,d.ToWarehouseId,d.CounterpartyId,
                    COALESCE(wf.Name,N'—'),COALESCE(wt.Name,N'—'),COALESCE(cp.Name,N'—'),u.FullName,d.Comment,
                    d.ShipperName,d.ShipperUnp,d.ShipperAddress,d.ConsigneeName,d.ConsigneeUnp,d.ConsigneeAddress,d.PayerName,d.PayerUnp,
                    d.Vehicle,d.Trailer,d.Driver,d.WaybillNumber,d.Basis,d.LoadingPoint,d.UnloadingPoint,d.ReleaseAllowedBy,d.HandedOverBy,
                    CASE WHEN d.LegacyOperationId IS NULL THEN 0 ELSE 1 END
                FROM dbo.StockDocuments d
                LEFT JOIN dbo.Warehouses wf ON wf.Id=d.FromWarehouseId LEFT JOIN dbo.Warehouses wt ON wt.Id=d.ToWarehouseId
                LEFT JOIN dbo.Counterparties cp ON cp.Id=d.CounterpartyId JOIN dbo.WarehouseUsers u ON u.Id=d.UserId
                WHERE d.Id=@id";
            Add(q, "@id", SqlDbType.Int, id);
            using var r = q.ExecuteReader();
            if (!r.Read()) return null;
            int? NullableInt(int i) => r.IsDBNull(i) ? null : r.GetInt32(i);
            document = new StockDocument
            {
                Id=r.GetInt32(0), Kind=(OperationKind)r.GetByte(1), Number=r.GetString(2), OccurredAt=r.GetDateTime(3),
                DocumentDate=r.IsDBNull(4) ? null : r.GetDateTime(4), FromWarehouseId=NullableInt(5), ToWarehouseId=NullableInt(6), CounterpartyId=NullableInt(7),
                FromWarehouse=r.GetString(8), ToWarehouse=r.GetString(9), CounterpartyName=r.GetString(10), UserName=r.GetString(11), Comment=r.GetString(12),
                ShipperName=r.GetString(13), ShipperUnp=r.GetString(14), ShipperAddress=r.GetString(15),
                ConsigneeName=r.GetString(16), ConsigneeUnp=r.GetString(17), ConsigneeAddress=r.GetString(18),
                PayerName=r.GetString(19), PayerUnp=r.GetString(20), Vehicle=r.GetString(21), Trailer=r.GetString(22), Driver=r.GetString(23),
                WaybillNumber=r.GetString(24), Basis=r.GetString(25), LoadingPoint=r.GetString(26), UnloadingPoint=r.GetString(27),
                ReleaseAllowedBy=r.GetString(28), HandedOverBy=r.GetString(29), IsLegacy=r.GetInt32(30)==1
            };
        }
        using (var q = c.CreateCommand())
        {
            q.CommandText = @"SELECT o.Id,o.ProductId,p.Name,p.Unit,o.Quantity,o.Price,o.DiscountPercent,o.VatRate,o.CargoPlaces,o.CargoMassKg,o.Comment
                FROM dbo.StockOperations o JOIN dbo.Products p ON p.Id=o.ProductId WHERE o.DocumentId=@id ORDER BY o.Id";
            Add(q, "@id", SqlDbType.Int, id);
            using var r = q.ExecuteReader();
            decimal? NullableDecimal(int i) => r.IsDBNull(i) ? null : r.GetDecimal(i);
            while (r.Read())
                document.Lines.Add(new DocumentLine
                {
                    Id=r.GetInt32(0), Number=document.Lines.Count + 1, ProductId=r.GetInt32(1), ProductName=r.GetString(2), Unit=r.GetString(3),
                    Quantity=r.GetDecimal(4), Price=NullableDecimal(5), DiscountPercent=NullableDecimal(6), VatRate=NullableDecimal(7),
                    CargoPlaces=NullableDecimal(8), CargoMassKg=NullableDecimal(9), Note=r.GetString(10)
                });
        }
        return document;
    }

    /// <summary>Ранее введённые значения для подсказок в окне документа.</summary>
    public DocumentSuggestions GetDocumentSuggestions()
    {
        using var c = Open();
        List<string> Distinct(string column)
        {
            if (column is not ("Vehicle" or "Trailer" or "Driver" or "PayerName")) throw new ArgumentException("Неизвестное поле.");
            using var q = c.CreateCommand();
            q.CommandText = $@"SELECT TOP (50) [{column}] FROM dbo.StockDocuments WHERE [{column}]<>N''
                GROUP BY [{column}] ORDER BY MAX(OccurredAt) DESC";
            using var r = q.ExecuteReader();
            var values = new List<string>();
            while (r.Read()) values.Add(r.GetString(0));
            return values;
        }
        return new DocumentSuggestions { Vehicles=Distinct("Vehicle"), Trailers=Distinct("Trailer"), Drivers=Distinct("Driver"), Payers=Distinct("PayerName") };
    }

    public List<StockRow> GetStocks(StockFilter filter)
    {
        Validation.Interval(filter.LastMovementFromInclusive, filter.LastMovementToExclusive);
        if (filter.MinimumQuantity is < 0) throw new ArgumentException("Минимальный остаток не может быть отрицательным.");
        var result = new List<StockRow>();
        using var c=Open(); using var q=c.CreateCommand();
        q.CommandText=@"SELECT b.ProductId,b.WarehouseId,p.Name,w.Name,p.Unit,b.Quantity,p.PurchasePrice,p.SalePrice,m.LastMovement
            FROM dbo.StockBalances b JOIN dbo.Products p ON p.Id=b.ProductId JOIN dbo.Warehouses w ON w.Id=b.WarehouseId
            OUTER APPLY (SELECT MAX(o.OccurredAt) AS LastMovement FROM dbo.StockOperations o
                WHERE o.ProductId=b.ProductId AND (o.FromWarehouseId=b.WarehouseId OR o.ToWarehouseId=b.WarehouseId)) m
            WHERE (@zero=1 OR b.Quantity>0) AND (@product IS NULL OR b.ProductId=@product) AND (@warehouse IS NULL OR b.WarehouseId=@warehouse)
              AND (@min IS NULL OR b.Quantity>=@min) AND (@from IS NULL OR m.LastMovement>=@from) AND (@to IS NULL OR m.LastMovement<@to)
              AND (p.Name LIKE @pattern ESCAPE N'~' OR p.Unit LIKE @pattern ESCAPE N'~' OR w.Name LIKE @pattern ESCAPE N'~')
            ORDER BY w.Name,p.Name,b.ProductId";
        Add(q,"@zero",SqlDbType.Bit,filter.IncludeZero); Add(q,"@product",SqlDbType.Int,filter.ProductId); Add(q,"@warehouse",SqlDbType.Int,filter.WarehouseId);
        Add(q,"@min",SqlDbType.Decimal,filter.MinimumQuantity); Add(q,"@from",SqlDbType.DateTime2,filter.LastMovementFromInclusive);
        Add(q,"@to",SqlDbType.DateTime2,filter.LastMovementToExclusive); Add(q,"@pattern",SqlDbType.NVarChar,Pattern(filter.Search),802);
        using var r=q.ExecuteReader();
        while(r.Read()) result.Add(new StockRow { ProductId=r.GetInt32(0),WarehouseId=r.GetInt32(1),ProductName=r.GetString(2),WarehouseName=r.GetString(3),
            Unit=r.GetString(4),Quantity=r.GetDecimal(5),PurchasePrice=r.GetDecimal(6),SalePrice=r.GetDecimal(7),LastMovement=r.IsDBNull(8)?null:r.GetDateTime(8) });
        return result;
    }

    public List<Movement> GetMovements(IEnumerable<int> productIds, DateTime endExclusive)
    {
        var ids=productIds.Distinct().Where(x=>x>0).ToArray(); var result=new List<Movement>();
        if (ids.Length == 0) return result;
        using var c=Open();
        foreach(var chunk in ids.Chunk(500))
        {
            using var q=c.CreateCommand();
            var parameters=chunk.Select((_,i)=>"@p"+i).ToArray();
            q.CommandText=$@"SELECT Id,ProductId,OccurredAt,Kind,Quantity,FromWarehouseId,ToWarehouseId
                FROM dbo.StockOperations WHERE ProductId IN ({string.Join(",",parameters)}) AND OccurredAt<@end ORDER BY OccurredAt,Id";
            Add(q,"@end",SqlDbType.DateTime2,endExclusive);
            for(var i=0;i<chunk.Length;i++) Add(q,parameters[i],SqlDbType.Int,chunk[i]);
            using var r=q.ExecuteReader();
            while(r.Read()) result.Add(new Movement(r.GetInt32(0),r.GetInt32(1),r.GetDateTime(2),(OperationKind)r.GetByte(3),r.GetDecimal(4),
                r.IsDBNull(5)?null:r.GetInt32(5),r.IsDBNull(6)?null:r.GetInt32(6)));
        }
        return result;
    }
}
