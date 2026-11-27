using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using kursa.Infrastructure;
using kursa.Views.Dialogs;
using Warehouse.Core.Models;
using Warehouse.Core.Services;

namespace kursa.Views;
public partial class OperationsView : UserControl
{
    private readonly WorkspaceContext _context;
    private readonly FilterIndicator _filters;
    private bool _loaded;
    private InvoiceDocument? _selectedInvoice;
    public OperationsView(WorkspaceContext context)
    {
        InitializeComponent(); _context=context;
        ReceiptButton.IsEnabled=TransferButton.IsEnabled=context.User.CanReceiptOrTransfer;
        ShipmentButton.IsEnabled=context.User.CanShip;
        _filters=new FilterIndicator(ApplyButton,FiltersExpander,()=>[FromPicker.Text,FromTimeBox.Text,ToPicker.Text,ToTimeBox.Text,
            Ui.SelectedId(ProductBox),Ui.SelectedId(WarehouseBox),Ui.SelectedId(PartyBox),Ui.SelectedId(KindBox),SearchBox.Text]);
        _filters.Track(FromPicker,FromTimeBox,ToPicker,ToTimeBox,ProductBox,WarehouseBox,PartyBox,KindBox,SearchBox);
    }
    public void Refresh()
    {
        if (_loaded && _filters.IsDirty) return;
        Ui.Run(() =>
        {
            var product=Ui.SelectedId(ProductBox); var warehouse=Ui.SelectedId(WarehouseBox); var party=Ui.SelectedId(PartyBox); var kind=Ui.SelectedId(KindBox);
            Ui.SetLookup(ProductBox,new[] {new LookupItem(null,"Все товары")}.Concat(_context.Repository.GetProducts().Select(p=>new LookupItem(p.Id,p.Name))),product);
            Ui.SetLookup(WarehouseBox,new[] {new LookupItem(null,"Все склады")}.Concat(_context.Repository.GetWarehouses().Select(w=>new LookupItem(w.Id,w.Name))),warehouse);
            Ui.SetLookup(PartyBox,new[] {new LookupItem(null,"Все контрагенты")}.Concat(_context.Repository.GetCounterparties().Select(p=>new LookupItem(p.Id,p.Name))),party);
            Ui.SetLookup(KindBox,[new(null,"Все операции"),new((int)OperationKind.Receipt,"Приход"),new((int)OperationKind.Shipment,"Реализация"),new((int)OperationKind.Transfer,"Перемещение")],kind);
            Apply();
        });
    }
    private void Apply() => Ui.Run(() =>
    {
        var type=Ui.SelectedId(KindBox);
        var interval=Ui.BuildInterval(Ui.ReadDate(FromPicker,"Дата с"),FromTimeBox.Text,Ui.ReadDate(ToPicker,"Дата по"),ToTimeBox.Text);
        var filter=new OperationFilter {FromInclusive=interval.From,ToExclusive=interval.ToExclusive,
            ProductId=Ui.SelectedId(ProductBox),WarehouseId=Ui.SelectedId(WarehouseBox),CounterpartyId=Ui.SelectedId(PartyBox),
            Kind=type.HasValue?(OperationKind)type.Value:null,Search=SearchBox.Text};
        var groups=_context.Repository.GetOperationBatches(filter); var lines=_context.Repository.GetOperations(filter);
        GridView.ItemsSource=groups; ProductLinesGrid.ItemsSource=lines;
        ClearDetails(); CountText.Text=$"Операций: {groups.Count}; товарных движений по фильтру: {lines.Count}. Г — новая группа, О — прежняя запись.";
        _loaded=true; _filters.MarkApplied();
    });
    private void Group_Changed(object sender,SelectionChangedEventArgs e)
    { if (GridView.SelectedItem is OperationBatchRow row) LoadDetails(row.BatchId,row.LegacyOperationId,row.DisplayId); else ClearDetails(); }
    private void Line_Changed(object sender,SelectionChangedEventArgs e)
    { if (ProductLinesGrid.SelectedItem is OperationRow row) LoadDetails(row.BatchId,row.BatchId is null?row.Id:null,row.GroupName); else ClearDetails(); }
    private void HistoryTabs_Changed(object sender,SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource,HistoryTabs)) return;
        if (HistoryTabs.SelectedIndex==0 && GridView.SelectedItem is OperationBatchRow group) LoadDetails(group.BatchId,group.LegacyOperationId,group.DisplayId);
        else if (HistoryTabs.SelectedIndex==1 && ProductLinesGrid.SelectedItem is OperationRow line) LoadDetails(line.BatchId,line.BatchId is null?line.Id:null,line.GroupName);
        else ClearDetails();
    }
    private void ClearDetails()
    {
        if (DetailsGrid is null || InvoiceButton is null) return;
        DetailsGrid.ItemsSource=null; InvoiceButton.IsEnabled=false; _selectedInvoice=null;
        DetailsTitle.Text="Выберите операцию — здесь появятся все её позиции";
    }
    private void LoadDetails(int? batchId,int? legacyId,string label)
    {
        // Ошибка чтения новой строки не должна оставлять активной накладную предыдущей операции.
        ClearDetails();
        Ui.Run(() =>
        {
        var items=_context.Repository.GetOperationItems(batchId,legacyId);
        var invoice=batchId.HasValue?_context.Repository.GetInvoice(batchId.Value):null;
        _selectedInvoice=invoice; InvoiceButton.IsEnabled=invoice is not null;
        var invoiceLabel=invoice is null ? "" : (string.IsNullOrWhiteSpace(invoice.Series) ? "" : invoice.Series+" ")+"№"+invoice.Number;
        DetailsTitle.Text=invoice is null ? $"Позиции операции {label}" : $"Позиции операции {label} · ТТН {invoiceLabel} · всего {invoice.TotalGross:N2} BYN";
        DetailsGrid.ItemsSource=invoice is null
            ? items.Select(DetailRow.FromMovement).ToList()
            : invoice.Lines.Select(DetailRow.FromInvoice).ToList();
        });
    }
    private sealed record DetailRow(string Name,string Quantity,string Unit,string Price,string Discount,string Vat,string Total,string Places,string Mass,string Note)
    {
        private static readonly CultureInfo Culture=CultureInfo.GetCultureInfo("ru-BY");
        public static DetailRow FromMovement(OperationRow x) => new(x.ProductName,x.Quantity.ToString("0.###",Culture),x.Unit,"—","—","—","—","—","—","");
        public static DetailRow FromInvoice(InvoiceLine x) => new(x.Name,x.Quantity?.ToString("0.###",Culture)??"—",x.Unit,x.UnitPrice.ToString("0.00",Culture),
            x.DiscountPercent.ToString("0.##",Culture)+" %",x.VatRate?.ToString("0.####",Culture)??"без НДС",x.GrossAmount.ToString("0.00",Culture),
            x.CargoPlaces?.ToString(Culture)??"—",x.MassTonnes?.ToString("0.000###",Culture)??"—",x.Note);
    }
    private void Invoice_Click(object sender,RoutedEventArgs e) => Ui.Run(() =>
    {
        var invoice=_selectedInvoice ?? throw new InvalidOperationException("Выберите реализацию с накладной.");
        var save=new SaveFileDialog {Title="Сохранить накладную",Filter="Документ Word (*.docx)|*.docx",FileName=DocxInvoiceWriter.DefaultFileName(invoice),AddExtension=true,DefaultExt=".docx"};
        if (save.ShowDialog(Window.GetWindow(this))!=true) return;
        DocxInvoiceWriter.Save(invoice,save.FileName);
        if (MessageBox.Show("Копия накладной создана. Открыть её?","Накладная",MessageBoxButton.YesNo,MessageBoxImage.Information)==MessageBoxResult.Yes)
            try { InvoiceFiles.Open(save.FileName); } catch(Exception ex) { Ui.Error("Файл создан, но открыть его не удалось: "+ex.Message); }
    });
    private void Apply_Click(object sender,RoutedEventArgs e) => Apply();
    private void Search_KeyDown(object sender,KeyEventArgs e) { if(e.Key==Key.Enter) {Apply();e.Handled=true;} }
    private void Reset_Click(object sender,RoutedEventArgs e)
    {
        FromPicker.SelectedDate=null; ToPicker.SelectedDate=null; ProductBox.SelectedIndex=WarehouseBox.SelectedIndex=PartyBox.SelectedIndex=KindBox.SelectedIndex=0;
        FromTimeBox.Text=ToTimeBox.Text=SearchBox.Text=""; Apply();
    }
    private void OpenOperation(OperationKind kind) => Ui.Run(() =>
    { if(new OperationWindow(_context,kind) {Owner=Window.GetWindow(this)}.ShowDialog()==true) Refresh(); });
    private void Receipt_Click(object sender,RoutedEventArgs e) => OpenOperation(OperationKind.Receipt);
    private void Shipment_Click(object sender,RoutedEventArgs e) => OpenOperation(OperationKind.Shipment);
    private void Transfer_Click(object sender,RoutedEventArgs e) => OpenOperation(OperationKind.Transfer);
}
