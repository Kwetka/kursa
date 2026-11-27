using System.IO;
using System.Diagnostics;
using Warehouse.Core.Data;
using Warehouse.Core.Models;
using Warehouse.Core.Services;

namespace kursa.Infrastructure;

public static class InvoiceFiles
{
    public static string SaveAutomatic(InvoiceDocument invoice, WarehouseRepository repository)
    {
        var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WarehouseCourse","Invoices",repository.InvoiceStorageKey);
        var path=Path.Combine(folder,DocxInvoiceWriter.DefaultFileName(invoice));
        DocxInvoiceWriter.Save(invoice,path,overwrite:false); return path;
    }
    public static void Open(string path)
    {
        Process.Start(new ProcessStartInfo(path) {UseShellExecute=true});
    }
}
