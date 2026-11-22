using System.Data;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Warehouse.Core.Models;
using Warehouse.Core.Services;

namespace Warehouse.Core.Data;

public static class DatabaseInitializer
{
    public static void Initialize(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.InitialCatalog) || builder.InitialCatalog.Equals("master", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Укажите отдельную базу данных приложения, не master.");
        try
        {
            // A failed login to a missing database must not poison the normal connection
            // pool's login-failure cache after CREATE DATABASE succeeds.
            using var probe = new SqlConnection(new SqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString);
            probe.Open();
        }
        catch (SqlException ex) when (ex.Number is 4060 or 911)
        {
            // Create the database only when it cannot be opened. An existing accessible
            // database does not require permissions on master or CREATE DATABASE.
            using var master = new SqlConnection(new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" }.ConnectionString);
            master.Open();
            using var create = master.CreateCommand();
            create.CommandText = "IF DB_ID(@db) IS NULL BEGIN DECLARE @sql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@db); EXEC(@sql); END";
            create.Parameters.Add("@db", SqlDbType.NVarChar, 128).Value = builder.InitialCatalog;
            create.CommandTimeout = 60;
            create.ExecuteNonQuery();
        }
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("Warehouse.Core.Data.Schema.sql")
            ?? throw new InvalidOperationException("Не найден файл SQL-схемы приложения.");
        using var reader = new StreamReader(stream);
        using var command = new SqlCommand(reader.ReadToEnd(), connection, transaction) { CommandTimeout = 60 };
        var created = Convert.ToBoolean(command.ExecuteScalar());
        if (created)
        {
            SeedUser(connection, transaction, "admin", "admin", "Администратор системы", UserRole.Admin);
            SeedUser(connection, transaction, "manager", "123", "Менеджер", UserRole.Manager);
            SeedUser(connection, transaction, "store", "123", "Кладовщик", UserRole.Storekeeper);
        }
        transaction.Commit();
    }

    private static void SeedUser(SqlConnection connection, SqlTransaction transaction, string login, string password, string name, UserRole role)
    {
        var (salt, hash) = PasswordHasher.Hash(password);
        using var command = new SqlCommand(@"INSERT dbo.WarehouseUsers(Login, FullName, Role, PasswordSalt, PasswordHash, PasswordIterations)
            VALUES (@login, @name, @role, @salt, @hash, @iterations);", connection, transaction);
        command.Parameters.Add("@login", SqlDbType.NVarChar, 80).Value = login;
        command.Parameters.Add("@name", SqlDbType.NVarChar, 200).Value = name;
        command.Parameters.Add("@role", SqlDbType.TinyInt).Value = (byte)role;
        command.Parameters.Add("@salt", SqlDbType.VarBinary, 16).Value = salt;
        command.Parameters.Add("@hash", SqlDbType.VarBinary, 32).Value = hash;
        command.Parameters.Add("@iterations", SqlDbType.Int).Value = PasswordHasher.Iterations;
        command.ExecuteNonQuery();
    }
}
