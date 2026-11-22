using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Warehouse.Core.Data;

namespace kursa.Infrastructure;

public static class SettingsStore
{
    private static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WarehouseCourse", "connection.dat");
    public static ConnectionSettings? Load()
    {
        if (!File.Exists(FilePath)) return null;
        var data = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<ConnectionSettings>(data) ?? throw new InvalidOperationException("Файл настроек пуст.");
    }
    public static void Save(ConnectionSettings settings)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(settings);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllBytes(FilePath, encrypted);
    }
}
