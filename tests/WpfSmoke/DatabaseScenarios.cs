// Optional disposable integration test. Uses only a randomly named WarehouseWpfSmoke_* database.
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using kursa.Infrastructure;
using kursa.Views;
using kursa.Views.Dialogs;
using Microsoft.Data.SqlClient;
using Warehouse.Core.Data;
using Warehouse.Core.Models;
using Warehouse.Core.Services;
using WarehouseEntity = Warehouse.Core.Models.Warehouse;

internal static partial class Program
{
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr FindWindow(string? className,string windowName);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr GetDlgItem(IntPtr window,int id);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window,StringBuilder text,int maxCount);
    private delegate bool EnumChildCallback(IntPtr window,IntPtr state);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool EnumChildWindows(IntPtr parent,EnumChildCallback callback,IntPtr state);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);

    private static Task<string> DismissMessageBox() => Task.Run(() =>
    {
        // Only the test process's own dialog can be dismissed; never touch another application's window.
        for (var n=0;n<600;n++)
        {
            foreach(var title in new[]{"Операция проведена","Склад · ошибка"})
            {
                var hwnd=FindWindow(null,title); if(hwnd==IntPtr.Zero) continue;
                GetWindowThreadProcessId(hwnd,out var pid); if(pid!=(uint)Environment.ProcessId) continue;
                // IDNO=7 for "Open?", otherwise IDOK=1. BM_CLICK does not open the document.
                var button=GetDlgItem(hwnd,7); if(button==IntPtr.Zero) button=GetDlgItem(hwnd,1);
                if(button!=IntPtr.Zero)
                {
                    var strings=new List<string>();
                    EnumChildCallback collect=(child,_) =>
                    {
                        var text=new StringBuilder(8192); GetWindowText(child,text,text.Capacity);
                        if(text.Length>0) strings.Add(text.ToString()); return true;
                    };
                    EnumChildWindows(hwnd,collect,IntPtr.Zero); GC.KeepAlive(collect);
                    PostMessage(button,0x00F5,IntPtr.Zero,IntPtr.Zero); return title+"|"+string.Join("|",strings);
                }
            }
            Thread.Sleep(100);
        }
        return "timeout";
    });

    private static void RunDatabaseScenarios()
    {
        var server=Environment.GetEnvironmentVariable("WAREHOUSE_WPF_TEST_SERVER")!;
        var user=Environment.GetEnvironmentVariable("WAREHOUSE_WPF_TEST_USER") ?? throw new InvalidOperationException("WAREHOUSE_WPF_TEST_USER is required.");
        var password=Environment.GetEnvironmentVariable("WAREHOUSE_WPF_TEST_PASSWORD") ?? throw new InvalidOperationException("WAREHOUSE_WPF_TEST_PASSWORD is required.");
        var preinitialized=Environment.GetEnvironmentVariable("WAREHOUSE_WPF_TEST_DATABASE");
        var database=string.IsNullOrWhiteSpace(preinitialized) ? "WarehouseWpfSmoke_"+Guid.NewGuid().ToString("N") : preinitialized;
        if (!System.Text.RegularExpressions.Regex.IsMatch(database,@"^WarehouseWpfSmoke_[0-9a-f]{32}$"))
            throw new InvalidOperationException("Test database must have a randomly generated WarehouseWpfSmoke_ name.");
        var settings=new ConnectionSettings {Server=server,Database=database,IntegratedSecurity=false,SqlLogin=user,SqlPassword=password,TrustServerCertificate=true};
        var cs=settings.BuildConnectionString();
        var auditDir=Path.Combine(Path.GetTempPath(),database);
        var repository=new WarehouseRepository(cs,auditDir,Guid.NewGuid());
        var invoiceDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WarehouseCourse","Invoices",repository.InvoiceStorageKey);
        try
        {
            if (string.IsNullOrWhiteSpace(preinitialized)) DatabaseInitializer.Initialize(cs);
            // The UI state test is not an authentication test: SQL supplies fixture identities, with no password written to output.
            AppUser ReadFixture(string login)
            {
                using var c=new SqlConnection(cs); c.Open(); using var q=c.CreateCommand();
                q.CommandText="SELECT Id,Login,FullName,Role FROM dbo.WarehouseUsers WHERE Login=@login AND IsActive=1";
                q.Parameters.AddWithValue("@login",login); using var r=q.ExecuteReader();
                if(!r.Read()) throw new InvalidOperationException("Missing WPF fixture user.");
                return new AppUser(r.GetInt32(0),r.GetString(1),r.GetString(2),(UserRole)r.GetByte(3));
            }
            using (var c=new SqlConnection(cs))
            {
                c.Open(); using var q=c.CreateCommand();
                q.CommandText=@"INSERT dbo.WarehouseUsers(Login,FullName,Role,PasswordSalt,PasswordHash,PasswordIterations,IsActive)
                    SELECT N'wpf_sales',N'Тест продаж',3,PasswordSalt,PasswordHash,PasswordIterations,1 FROM dbo.WarehouseUsers WHERE Login=N'manager'";
                q.ExecuteNonQuery();
            }
            var admin=ReadFixture("admin"); var sales=ReadFixture("wpf_sales");
            repository.SaveOrganization(new PartyDetails {Name="Организация",Address="Минск",Unp="123456789"},admin);
            repository.SaveProduct(new Product {Name="Позиция A",Unit="шт",PurchasePrice=2,SalePrice=10,UnitWeightKg=2,UnitsPerCargoPlace=2},admin);
            repository.SaveProduct(new Product {Name="Позиция B",Unit="упак",PurchasePrice=3,SalePrice=20},admin);
            repository.SaveWarehouse(new WarehouseEntity {Name="Склад",Address="Минск",ManagerName="Иванов",ManagerContact="123"},admin);
            repository.SaveCounterparty(new Counterparty {Name="Поставщик",IsSupplier=true,Address="Минск"},admin);
            repository.SaveCounterparty(new Counterparty {Name="Покупатель",IsSupplier=false,Address="Брест",Unp="123"},admin);
            var products=repository.GetProducts(); var warehouse=repository.GetWarehouses().Single(); var parties=repository.GetCounterparties();
            var buyer=parties.Single(x=>!x.IsSupplier); var supplier=parties.Single(x=>x.IsSupplier);
            repository.CreateBatch(new BatchOperationInput(OperationKind.Receipt,null,warehouse.Id,supplier.Id,"WPF fixture",
                [new(products[0].Id,10),new(products[1].Id,10)]),admin);
            var context=new WorkspaceContext(repository,sales);
            var sale=new OperationWindow(context,OperationKind.Shipment);
            Check(Named<TabItem>(sale,"InvoiceTab").Visibility==Visibility.Visible && Named<ComboBox>(sale,"PriceModeBox").Items.Count==3
                && Named<ComboBox>(sale,"PriceModeBox").SelectedIndex==-1,"sale requires one of three explicit VAT modes");
            Check(Named<CheckBox>(sale,"AutoNumberBox").IsChecked==true && Named<TextBox>(sale,"NumberBox").IsReadOnly
                && Named<TextBox>(sale,"NumberBox").Text.Length>0 && Named<TextBox>(sale,"SeriesBox").Text=="",
                "suggested number is automatic and series is optional");
            Check(Named<TextBox>(sale,"SenderNameBox").Text=="Организация" && Named<TextBox>(sale,"RecipientNameBox").Text=="Покупатель"
                && Named<TextBox>(sale,"RecipientNameBox").IsReadOnly && Named<TextBox>(sale,"PayerNameBox").IsReadOnly,
                "sales manager uses administrator-maintained organization and counterparty details");
            var consignee=Named<ComboBox>(sale,"RecipientPartyBox");
            Named<Button>(sale,"PayerRecipientButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            consignee.SelectedItem=consignee.Items.OfType<Counterparty>().Single(x=>x.Id==supplier.Id);
            Check(Named<TextBox>(sale,"RecipientNameBox").Text=="Поставщик" && Named<TextBox>(sale,"UnloadingBox").Text==supplier.Address
                && Named<TextBox>(sale,"PayerNameBox").Text=="Поставщик",
                "consignee can be another counterparty and payer follows when chosen as recipient");
            consignee.SelectedItem=consignee.Items.OfType<Counterparty>().Single(x=>x.Id==buyer.Id);
            var adminSale=new OperationWindow(new WorkspaceContext(repository,admin),OperationKind.Shipment);
            Check(!Named<TextBox>(adminSale,"RecipientNameBox").IsReadOnly && !Named<TextBox>(adminSale,"PayerNameBox").IsReadOnly,
                "administrator can override invoice party details");
            var receipt=new OperationWindow(new WorkspaceContext(repository,admin),OperationKind.Receipt);
            Check(Named<TabItem>(receipt,"InvoiceTab").Visibility==Visibility.Collapsed,"receipt does not display sale invoice tab");

            Exception? automationError=null; Task<string>? dismissal=null;
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(() =>
            {
                try
                {
                    // Optionally capture the actual laid-out dialog for visual review.
                    var screenshot=Environment.GetEnvironmentVariable("WAREHOUSE_WPF_CAPTURE");
                    if (!string.IsNullOrWhiteSpace(screenshot))
                    {
                        sale.UpdateLayout(); var bitmap=new RenderTargetBitmap((int)sale.ActualWidth,(int)sale.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);
                        bitmap.Render(sale); var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using (var file=File.Create(screenshot)) encoder.Save(file);
                        Named<TabControl>(sale,"OperationTabs").SelectedIndex=1; sale.UpdateLayout();
                        bitmap=new RenderTargetBitmap((int)sale.ActualWidth,(int)sale.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);
                        bitmap.Render(sale); encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var invoiceFile=File.Create(Path.Combine(Path.GetDirectoryName(screenshot)!,Path.GetFileNameWithoutExtension(screenshot)+"-invoice.png"));
                        encoder.Save(invoiceFile);
                    }
                    var mode=Named<ComboBox>(sale,"PriceModeBox"); mode.SelectedIndex=0;
                    void AddProduct(int id,string quantity,bool withVat)
                    {
                        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(() =>
                        {
                            var editor=Application.Current.Windows.OfType<OperationItemWindow>().Single(x=>x.IsVisible);
                            var productBox=Named<ComboBox>(editor,"ProductBox");
                            productBox.SelectedItem=productBox.Items.OfType<Product>().Single(x=>x.Id==id);
                            Named<TextBox>(editor,"QuantityBox").Text=quantity;
                            if (withVat) Named<TextBox>(editor,"VatBox").Text="20";
                            Named<Button>(editor,"ItemSaveButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                        }));
                        Named<Button>(sale,"AddItemButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    }
                    AddProduct(products[0].Id,"2",true);
                    Named<TextBox>(sale,"TransportVatBox").Text="20";
                    mode.SelectedIndex=2;
                    Check(!Named<TextBox>(sale,"TransportVatBox").IsEnabled && Named<TextBox>(sale,"TransportVatBox").Text==""
                        && ((IEnumerable<OperationItemView>)Named<DataGrid>(sale,"ItemsGrid").ItemsSource).Single().Input.VatRate is null,
                        "switching to without VAT clears line and transport VAT");
                    AddProduct(products[1].Id,"3",false);
                    Check(Named<DataGrid>(sale,"ItemsGrid").Items.Count==2,"UI added two editable sale positions");
                    Named<TextBox>(sale,"TransportPriceBox").Text="10";
                    var payer=Named<ComboBox>(sale,"PayerPartyBox"); payer.SelectedItem=payer.Items.OfType<Counterparty>().Single(x=>x.Id==buyer.Id);
                    Check(Named<TextBox>(sale,"PayerNameBox").Text=="Покупатель","payer can be selected from administrator's counterparties");
                    var auto=Named<CheckBox>(sale,"AutoNumberBox"); auto.IsChecked=false; auto.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    Check(!Named<TextBox>(sale,"NumberBox").IsReadOnly,"suggested number can be made editable");
                    Named<TextBox>(sale,"NumberBox").Text="WPF-123";
                    dismissal=DismissMessageBox();
                    Named<Button>(sale,"SaveButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    if (sale.IsVisible) sale.Close();
                }
                catch(Exception ex) { automationError=ex; if(sale.IsVisible) sale.Close(); }
            }));
            var success=sale.ShowDialog();
            if (automationError is not null) throw new InvalidOperationException("WPF dialog automation failed",automationError);
            Check(success==true && dismissal is not null && dismissal.GetAwaiter().GetResult().StartsWith("Операция проведена|"),
                "UI conducts sale and reports post-commit DOCX creation");
            var group=repository.GetOperationBatches(new OperationFilter {Kind=OperationKind.Shipment}).Single();
            var invoice=repository.GetInvoice(group.BatchId!.Value)!;
            Check(group.ItemCount==2 && invoice.Lines.Count==3 && invoice.PriceMode==VatPriceMode.WithoutVat && invoice.Series==""
                && invoice.Number=="WPF-123" && invoice.TotalGross==90m,"UI persists two goods and non-stock transport in one invoice");
            Check(Directory.Exists(invoiceDir) && Directory.GetFiles(invoiceDir,"*.docx").Length==1,"UI creates editable DOCX in local invoice folder");

            // Simulate a pre-existing edited file: SQL must still commit, and the UI must not overwrite or ask to repeat the sale.
            var collision=Path.Combine(invoiceDir,DocxInvoiceWriter.DefaultFileName(new InvoiceDocument {Id=invoice.Id+1,Series="",Number="WPF-124"}));
            File.WriteAllText(collision,"already edited by the user");
            var exportFailureSale=new OperationWindow(context,OperationKind.Shipment);
            Exception? secondError=null; Task<string>? secondDismissal=null;
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>
            {
                try
                {
                    Named<ComboBox>(exportFailureSale,"PriceModeBox").SelectedIndex=2;
                    Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>
                    {
                        var editor=Application.Current.Windows.OfType<OperationItemWindow>().Single(x=>x.IsVisible);
                        Named<TextBox>(editor,"QuantityBox").Text="1";
                        Named<Button>(editor,"ItemSaveButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    }));
                    Named<Button>(exportFailureSale,"AddItemButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    var auto=Named<CheckBox>(exportFailureSale,"AutoNumberBox"); auto.IsChecked=false; auto.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    Named<TextBox>(exportFailureSale,"NumberBox").Text="WPF-124";
                    secondDismissal=DismissMessageBox();
                    Named<Button>(exportFailureSale,"SaveButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    if(exportFailureSale.IsVisible) exportFailureSale.Close();
                }
                catch(Exception ex) { secondError=ex; if(exportFailureSale.IsVisible) exportFailureSale.Close(); }
            }));
            var secondResult=exportFailureSale.ShowDialog();
            if(secondError is not null) throw new InvalidOperationException("WPF export-failure scenario failed",secondError);
            var groupsAfterFailure=repository.GetOperationBatches(new OperationFilter {Kind=OperationKind.Shipment});
            var recoveredGroup=groupsAfterFailure.Single(x=>x.InvoiceLabel=="№WPF-124");
            var recoveredId=recoveredGroup.BatchId ?? throw new InvalidOperationException("Recovered sale has no batch ID.");
            Check(secondResult==true && secondDismissal is not null && secondDismissal.GetAwaiter().GetResult().StartsWith("Операция проведена|")
                && secondDismissal.GetAwaiter().GetResult().Contains("DOCX не создан")
                && repository.GetInvoice(recoveredId)!.Id==invoice.Id+1 && File.ReadAllText(collision)=="already edited by the user",
                "export failure leaves sale committed, warns, and does not overwrite an existing document");
            var retry=Path.Combine(invoiceDir,"retry.docx"); DocxInvoiceWriter.Save(repository.GetInvoice(recoveredId)!,retry);
            Check(File.Exists(retry),"document can be regenerated from the saved SQL snapshot after export failure");
            var journal=new OperationsView(context); journal.Refresh();
            var groups=Named<DataGrid>(journal,"GridView"); groups.SelectedItem=((IEnumerable<OperationBatchRow>)groups.ItemsSource).Single(x=>x.BatchId==group.BatchId);
            Check(Named<DataGrid>(journal,"DetailsGrid").Items.Count==3 && Named<Button>(journal,"InvoiceButton").IsEnabled,
                "selecting sale group shows all goods and transport and enables invoice export");
            // A read error for another row must not leave the previous invoice active.
            string originalPayload;
            using (var c=new SqlConnection(cs))
            {
                c.Open(); using var q=c.CreateCommand(); q.CommandText="SELECT Payload FROM dbo.Invoices WHERE BatchId=@id";
                q.Parameters.AddWithValue("@id",recoveredId); originalPayload=(string)q.ExecuteScalar()!;
                q.CommandText="UPDATE dbo.Invoices SET Payload=N'{\"BatchId\":\"invalid\"}' WHERE BatchId=@id"; q.ExecuteNonQuery();
            }
            try
            {
                var errorDialog=DismissMessageBox();
                groups.SelectedItem=((IEnumerable<OperationBatchRow>)groups.ItemsSource).Single(x=>x.BatchId==recoveredId);
                Check(errorDialog.GetAwaiter().GetResult().StartsWith("Склад · ошибка|") && !Named<Button>(journal,"InvoiceButton").IsEnabled
                    && Named<DataGrid>(journal,"DetailsGrid").Items.Count==0,"failed selection clears previous invoice and details");
            }
            finally
            {
                using var c=new SqlConnection(cs); c.Open(); using var q=c.CreateCommand(); q.CommandText="UPDATE dbo.Invoices SET Payload=@payload WHERE BatchId=@id";
                q.Parameters.AddWithValue("@payload",originalPayload); q.Parameters.AddWithValue("@id",recoveredId); q.ExecuteNonQuery();
            }
            Named<TabControl>(journal,"HistoryTabs").SelectedIndex=1;
            var movements=Named<DataGrid>(journal,"ProductLinesGrid");
            movements.SelectedItem=((IEnumerable<OperationRow>)movements.ItemsSource).First(x=>x.BatchId==group.BatchId);
            Check(Named<DataGrid>(journal,"DetailsGrid").Items.Count==3,"selecting individual movement still shows the entire sale");
        }
        finally
        {
            SqlConnection.ClearAllPools();
            using (var c=new SqlConnection(new SqlConnectionStringBuilder(cs) {InitialCatalog="master"}.ConnectionString))
            {
                c.Open(); using var q=c.CreateCommand();
                q.CommandText=$"IF DB_ID(N'{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END";
                q.ExecuteNonQuery();
            }
            if(Directory.Exists(auditDir)) Directory.Delete(auditDir,true);
            if(Directory.Exists(invoiceDir)) Directory.Delete(invoiceDir,true);
        }
    }
}
