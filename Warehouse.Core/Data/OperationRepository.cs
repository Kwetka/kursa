using System.Data;
using Microsoft.Data.SqlClient;
using Warehouse.Core.Models;
using Warehouse.Core.Services;

namespace Warehouse.Core.Data;

public sealed partial class WarehouseRepository
{
    /// <summary>Запись в журнале и изменение обоих остатков выполняются как одна транзакция.</summary>
    public int CreateOperation(OperationInput operation, AppUser actor)
    {
        Validation.Operation(operation);
        if (actor.Id <= 0) throw new UnauthorizedAccessException("Сначала войдите в программу.");
        using var c = Open();
        using var tx = c.BeginTransaction(IsolationLevel.Serializable);
        using (var q = new SqlCommand(@"DECLARE @result int, @resource nvarchar(255) = N'WarehouseCourse.Product:' + CONVERT(nvarchar(20), @product);
            EXEC @result = sys.sp_getapplock @Resource=@resource, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=15000;
            SELECT @result;", c, tx))
        {
            Add(q, "@product", SqlDbType.Int, operation.ProductId);
            if (Convert.ToInt32(q.ExecuteScalar()) < 0) throw new InvalidOperationException("Товар занят другой операцией. Повторите попытку.");
        }
        if (operation.CounterpartyId is int partyId)
        {
            using var q = new SqlCommand("SELECT IsSupplier FROM dbo.Counterparties WITH (UPDLOCK,HOLDLOCK) WHERE Id=@id", c, tx);
            Add(q, "@id", SqlDbType.Int, partyId);
            var value = q.ExecuteScalar();
            if (value is null || Convert.ToBoolean(value) != (operation.Kind == OperationKind.Receipt))
                throw new InvalidOperationException("Для прихода нужен поставщик, для расхода — покупатель.");
        }
        if (operation.FromWarehouseId is int source)
        {
            using var q = new SqlCommand(@"UPDATE dbo.StockBalances SET Quantity = Quantity - @qty
                OUTPUT INSERTED.Quantity WHERE ProductId=@product AND WarehouseId=@warehouse AND Quantity >= @qty", c, tx);
            Add(q, "@qty", SqlDbType.Decimal, operation.Quantity); Add(q, "@product", SqlDbType.Int, operation.ProductId); Add(q, "@warehouse", SqlDbType.Int, source);
            if (q.ExecuteScalar() is null) throw new InvalidOperationException("Недостаточно товара на исходном складе.");
        }
        if (operation.ToWarehouseId is int destination)
        {
            using var q = new SqlCommand(@"IF EXISTS (SELECT 1 FROM dbo.StockBalances WHERE ProductId=@product AND WarehouseId=@warehouse)
                UPDATE dbo.StockBalances SET Quantity=Quantity+@qty WHERE ProductId=@product AND WarehouseId=@warehouse;
                ELSE INSERT dbo.StockBalances(ProductId,WarehouseId,Quantity) VALUES (@product,@warehouse,@qty);", c, tx);
            Add(q, "@qty", SqlDbType.Decimal, operation.Quantity); Add(q, "@product", SqlDbType.Int, operation.ProductId); Add(q, "@warehouse", SqlDbType.Int, destination);
            q.ExecuteNonQuery();
        }
        int id;
        using (var q = new SqlCommand(@"INSERT dbo.StockOperations(Kind,ProductId,Quantity,FromWarehouseId,ToWarehouseId,CounterpartyId,Comment,UserId)
            OUTPUT INSERTED.Id VALUES (@kind,@product,@qty,@from,@to,@party,@comment,@user)", c, tx))
        {
            Add(q, "@kind", SqlDbType.TinyInt, (byte)operation.Kind); Add(q, "@product", SqlDbType.Int, operation.ProductId);
            Add(q, "@qty", SqlDbType.Decimal, operation.Quantity); Add(q, "@from", SqlDbType.Int, operation.FromWarehouseId);
            Add(q, "@to", SqlDbType.Int, operation.ToWarehouseId); Add(q, "@party", SqlDbType.Int, operation.CounterpartyId);
            Add(q, "@comment", SqlDbType.NVarChar, Validation.Optional(operation.Comment, "Комментарий", 500), 500);
            Add(q, "@user", SqlDbType.Int, actor.Id);
            id = Convert.ToInt32(q.ExecuteScalar());
        }
        tx.Commit();
        return id;
    }

    public List<OperationRow> GetOperations(OperationFilter filter)
    {
        Validation.Interval(filter.FromInclusive, filter.ToExclusive);
        var result = new List<OperationRow>();
        using var c = Open(); using var q = c.CreateCommand();
        q.CommandText = @"SELECT o.Id,o.OccurredAt,o.Kind,p.Name,o.Quantity,p.Unit,
            COALESCE(wf.Name,N'—'),COALESCE(wt.Name,N'—'),COALESCE(cp.Name,N'—'),o.Comment,u.FullName
            FROM dbo.StockOperations o JOIN dbo.Products p ON p.Id=o.ProductId
            LEFT JOIN dbo.Warehouses wf ON wf.Id=o.FromWarehouseId LEFT JOIN dbo.Warehouses wt ON wt.Id=o.ToWarehouseId
            LEFT JOIN dbo.Counterparties cp ON cp.Id=o.CounterpartyId JOIN dbo.WarehouseUsers u ON u.Id=o.UserId
            WHERE (@from IS NULL OR o.OccurredAt>=@from) AND (@to IS NULL OR o.OccurredAt<@to)
              AND (@product IS NULL OR o.ProductId=@product) AND (@warehouse IS NULL OR o.FromWarehouseId=@warehouse OR o.ToWarehouseId=@warehouse)
              AND (@party IS NULL OR o.CounterpartyId=@party) AND (@kind IS NULL OR o.Kind=@kind)
              AND (p.Name LIKE @pattern ESCAPE N'~' OR wf.Name LIKE @pattern ESCAPE N'~' OR wt.Name LIKE @pattern ESCAPE N'~'
                   OR cp.Name LIKE @pattern ESCAPE N'~' OR o.Comment LIKE @pattern ESCAPE N'~' OR u.FullName LIKE @pattern ESCAPE N'~'
                   OR CONVERT(nvarchar(20),o.Id) LIKE @pattern ESCAPE N'~')
            ORDER BY o.OccurredAt DESC,o.Id DESC";
        Add(q,"@from",SqlDbType.DateTime2,filter.FromInclusive); Add(q,"@to",SqlDbType.DateTime2,filter.ToExclusive);
        Add(q,"@product",SqlDbType.Int,filter.ProductId); Add(q,"@warehouse",SqlDbType.Int,filter.WarehouseId); Add(q,"@party",SqlDbType.Int,filter.CounterpartyId);
        Add(q,"@kind",SqlDbType.TinyInt,filter.Kind.HasValue ? (byte)filter.Kind.Value : null); Add(q,"@pattern",SqlDbType.NVarChar,Pattern(filter.Search), 802);
        using var r=q.ExecuteReader();
        while(r.Read()) result.Add(new OperationRow { Id=r.GetInt32(0), OccurredAt=r.GetDateTime(1), Kind=(OperationKind)r.GetByte(2),
            ProductName=r.GetString(3),Quantity=r.GetDecimal(4),Unit=r.GetString(5),FromWarehouse=r.GetString(6),ToWarehouse=r.GetString(7),
            CounterpartyName=r.GetString(8),Comment=r.GetString(9),UserName=r.GetString(10) });
        return result;
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
