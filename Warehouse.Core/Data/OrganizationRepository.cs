using System.Data;
using Microsoft.Data.SqlClient;
using Warehouse.Core.Models;
using Warehouse.Core.Services;

namespace Warehouse.Core.Data;

public sealed partial class WarehouseRepository
{
    private static PartyDetails ReadOrganization(SqlConnection c, SqlTransaction? tx = null)
    {
        using var q = new SqlCommand("SELECT Name,Address,Unp FROM dbo.OrganizationProfile WHERE Id=1", c, tx);
        using var r = q.ExecuteReader();
        if (!r.Read()) throw new InvalidOperationException("Не найдены реквизиты организации.");
        return new PartyDetails { Name=r.GetString(0), Address=r.GetString(1), Unp=r.GetString(2) };
    }

    public PartyDetails GetOrganization()
    {
        using var c=Open(); return ReadOrganization(c);
    }

    public void SaveOrganization(PartyDetails value, AppUser actor)
    {
        ArgumentNullException.ThrowIfNull(value);
        var after=new PartyDetails { Name=Validation.Required(value.Name,"Наименование организации",200),
            Address=Validation.Required(value.Address,"Адрес организации",400), Unp=Validation.Optional(value.Unp,"УНП",30) };
        using var c=Open(); using var tx=c.BeginTransaction(IsolationLevel.Serializable);
        var actual=RequireActor(c,tx,actor,u=>u.CanManageWarehousesAndParties);
        var before=ReadOrganization(c,tx);
        using (var q=new SqlCommand("UPDATE dbo.OrganizationProfile SET Name=@name,Address=@address,Unp=@unp WHERE Id=1",c,tx))
        {
            Add(q,"@name",SqlDbType.NVarChar,after.Name,200); Add(q,"@address",SqlDbType.NVarChar,after.Address,400);
            Add(q,"@unp",SqlDbType.NVarChar,after.Unp,30);
            if (q.ExecuteNonQuery()!=1) throw new InvalidOperationException("Не найдены реквизиты организации.");
        }
        WriteAudit(c,tx,actual,"Изменены реквизиты организации","Организация","1",new {Before=before,After=after});
        tx.Commit(); SyncAuditFile();
    }
}
