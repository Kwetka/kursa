using Microsoft.Data.SqlClient;

namespace Warehouse.Core.Data;

public sealed class ConnectionSettings
{
    public string Server { get; set; } = @"(localdb)\MSSQLLocalDB";
    public string Database { get; set; } = "WarehouseCourse";
    public bool IntegratedSecurity { get; set; } = true;
    public string SqlLogin { get; set; } = "";
    public string SqlPassword { get; set; } = "";
    // For LocalDB / local Express self-signed certificates. Uncheck for a trusted server certificate.
    public bool TrustServerCertificate { get; set; } = true;

    public string BuildConnectionString()
    {
        if (string.IsNullOrWhiteSpace(Server)) throw new ArgumentException("Укажите имя SQL Server.");
        if (string.IsNullOrWhiteSpace(Database) || Database.Length > 128 || Database.Any(char.IsControl))
            throw new ArgumentException("Укажите имя базы данных (от 1 до 128 символов). ");
        if (!IntegratedSecurity && string.IsNullOrWhiteSpace(SqlLogin))
            throw new ArgumentException("Укажите логин SQL Server.");
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = Server.Trim(), InitialCatalog = Database.Trim(),
            IntegratedSecurity = IntegratedSecurity, Encrypt = true,
            TrustServerCertificate = TrustServerCertificate, ConnectTimeout = 8,
            ApplicationName = "WarehouseCourse"
        };
        if (!IntegratedSecurity) { builder.UserID = SqlLogin.Trim(); builder.Password = SqlPassword; }
        return builder.ConnectionString;
    }
}
