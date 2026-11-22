using System.Data;
using Microsoft.Data.SqlClient;
using Warehouse.Core.Models;
using Warehouse.Core.Services;

namespace Warehouse.Core.Data;

public sealed partial class WarehouseRepository
{
    private static void LockAccounts(SqlConnection c, SqlTransaction tx)
    {
        using var q=new SqlCommand(@"DECLARE @result int;
            EXEC @result=sys.sp_getapplock @Resource=N'WarehouseCourse.Accounts',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=15000;
            IF @result<0 THROW 51011, N'Не удалось заблокировать управление пользователями.', 1;",c,tx);
        q.ExecuteNonQuery();
    }

    public List<UserAccount> GetUsers(AppUser actor)
    {
        var result=new List<UserAccount>();
        using var c=Open(); using var tx=c.BeginTransaction();
        RequireActor(c,tx,actor,u=>u.CanManageUsers);
        using (var q=new SqlCommand("SELECT Id,Login,FullName,Role FROM dbo.WarehouseUsers WHERE IsActive=1 ORDER BY Login,Id",c,tx))
        using (var r=q.ExecuteReader())
            while (r.Read()) result.Add(ReadAccount(r));
        tx.Commit(); return result;
    }
    private static UserAccount ReadAccount(SqlDataReader r) => new() {Id=r.GetInt32(0),Login=r.GetString(1),FullName=r.GetString(2),Role=(UserRole)r.GetByte(3)};
    private static UserAccount? FindAccount(SqlConnection c, SqlTransaction tx, int id)
    {
        using var q=new SqlCommand("SELECT Id,Login,FullName,Role FROM dbo.WarehouseUsers WITH (UPDLOCK,HOLDLOCK) WHERE Id=@id AND IsActive=1",c,tx);
        Add(q,"@id",SqlDbType.Int,id); using var r=q.ExecuteReader(); return r.Read() ? ReadAccount(r) : null;
    }
    private static void EnsureAnotherAdmin(SqlConnection c, SqlTransaction tx)
    {
        using var q=new SqlCommand("SELECT COUNT(*) FROM dbo.WarehouseUsers WITH (UPDLOCK,HOLDLOCK) WHERE IsActive=1 AND Role=0",c,tx);
        if (Convert.ToInt32(q.ExecuteScalar()) <= 1) throw new InvalidOperationException("Нельзя удалить или изменить роль последнего администратора.");
    }

    public void SaveUser(UserAccount user, string? newPassword, AppUser actor)
    {
        var login=Validation.Required(user.Login,"Логин",80);
        var name=Validation.Required(user.FullName,"ФИО",200);
        if (!Enum.IsDefined(user.Role)) throw new ArgumentException("Выберите роль пользователя.");
        bool changePassword = !string.IsNullOrEmpty(newPassword);
        if (user.Id == 0 || changePassword) Validation.Password(newPassword);
        byte[]? salt=null, hash=null;
        if (changePassword) (salt,hash)=PasswordHasher.Hash(newPassword!);
        try
        {
            using var c=Open(); using var tx=c.BeginTransaction(IsolationLevel.Serializable);
            LockAccounts(c,tx);
            var actual=RequireActor(c,tx,actor,u=>u.CanManageUsers);
            var before=user.Id == 0 ? null : FindAccount(c,tx,user.Id) ?? throw new InvalidOperationException("Учётная запись не найдена.");
            if (before is not null && before.Role == UserRole.Admin && user.Role != UserRole.Admin)
            {
                if (before.Id == actual.Id) throw new InvalidOperationException("Нельзя изменить роль собственной учётной записи.");
                EnsureAnotherAdmin(c,tx);
            }
            int id;
            using (var q=new SqlCommand(user.Id == 0
                ? @"INSERT dbo.WarehouseUsers(Login,FullName,Role,PasswordSalt,PasswordHash,PasswordIterations)
                    OUTPUT INSERTED.Id VALUES (@login,@name,@role,@salt,@hash,@iterations)"
                : @"UPDATE dbo.WarehouseUsers SET Login=@login,FullName=@name,Role=@role,
                    PasswordSalt=CASE WHEN @change=1 THEN @salt ELSE PasswordSalt END,
                    PasswordHash=CASE WHEN @change=1 THEN @hash ELSE PasswordHash END,
                    PasswordIterations=CASE WHEN @change=1 THEN @iterations ELSE PasswordIterations END
                    OUTPUT INSERTED.Id WHERE Id=@id AND IsActive=1",c,tx))
            {
                Add(q,"@id",SqlDbType.Int,user.Id); Add(q,"@login",SqlDbType.NVarChar,login,80); Add(q,"@name",SqlDbType.NVarChar,name,200);
                Add(q,"@role",SqlDbType.TinyInt,(byte)user.Role); Add(q,"@change",SqlDbType.Bit,changePassword);
                Add(q,"@salt",SqlDbType.VarBinary,salt,16); Add(q,"@hash",SqlDbType.VarBinary,hash,32);
                Add(q,"@iterations",SqlDbType.Int,PasswordHasher.Iterations);
                var value=q.ExecuteScalar(); if (value is null) throw new InvalidOperationException("Учётная запись не найдена."); id=Convert.ToInt32(value);
            }
            var after=new UserAccount {Id=id,Login=login,FullName=name,Role=user.Role};
            WriteAudit(c,tx,actual,before is null ? "Создана учётная запись" : "Изменена учётная запись", "Учётная запись", id.ToString(),
                new { Before=before,After=after,PasswordChanged=changePassword });
            tx.Commit(); SyncAuditFile();
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            throw new InvalidOperationException("Этот логин уже занят, в том числе удалённой учётной записью.",ex);
        }
    }

    public void DeleteUser(int id, AppUser actor)
    {
        using var c=Open(); using var tx=c.BeginTransaction(IsolationLevel.Serializable);
        LockAccounts(c,tx);
        var actual=RequireActor(c,tx,actor,u=>u.CanManageUsers);
        var before=FindAccount(c,tx,id) ?? throw new InvalidOperationException("Учётная запись уже удалена или не найдена.");
        if (before.Id == actual.Id) throw new InvalidOperationException("Нельзя удалить собственную учётную запись.");
        if (before.Role == UserRole.Admin) EnsureAnotherAdmin(c,tx);
        using (var q=new SqlCommand("UPDATE dbo.WarehouseUsers SET IsActive=0 WHERE Id=@id AND IsActive=1",c,tx))
        {
            Add(q,"@id",SqlDbType.Int,id);
            if (q.ExecuteNonQuery()!=1) throw new InvalidOperationException("Учётная запись уже удалена или не найдена.");
        }
        WriteAudit(c,tx,actual,"Удалена учётная запись", "Учётная запись", id.ToString(),new { Before=before,IsActive=false });
        tx.Commit(); SyncAuditFile();
    }
}
