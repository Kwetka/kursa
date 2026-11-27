using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using kursa.Infrastructure;
using Warehouse.Core.Models;
using WarehouseEntity = Warehouse.Core.Models.Warehouse;

namespace kursa.Views.Dialogs;
public partial class OperationWindow : Window
{
    private readonly WorkspaceContext _context;
    private readonly OperationKind _kind;
    private readonly List<Product> _products;
    private readonly ObservableCollection<OperationItemView> _items=[];
    private bool _ready;
    private int? _payerPartyId;
    private bool _payerIsSender, _payerFollowsRecipient;
    public OperationWindow(WorkspaceContext context, OperationKind kind)
    {
        InitializeComponent(); _context=context; _kind=kind;
        Heading.Text=kind switch {OperationKind.Receipt=>"Поступление товаров",OperationKind.Shipment=>"Реализация товаров и ТТН",_=>"Перемещение товаров"};
        _products=context.Repository.GetProducts(); var warehouses=context.Repository.GetWarehouses();
        FromBox.ItemsSource=warehouses; ToBox.ItemsSource=warehouses;
        FromBox.SelectedIndex=warehouses.Count>0?0:-1;
        ToBox.SelectedIndex=warehouses.Count>0?(kind==OperationKind.Transfer && warehouses.Count>1?1:0):-1;
        FromPanel.Visibility=kind==OperationKind.Receipt?Visibility.Collapsed:Visibility.Visible;
        ToPanel.Visibility=kind==OperationKind.Shipment?Visibility.Collapsed:Visibility.Visible;
        PartyPanel.Visibility=kind==OperationKind.Transfer?Visibility.Collapsed:Visibility.Visible;
        PartyLabel.Text=kind==OperationKind.Receipt?"Поставщик":"Покупатель";
        var allParties=context.Repository.GetCounterparties();
        var parties=allParties.Where(x=>x.IsSupplier==(kind==OperationKind.Receipt)).ToList();
        PartyBox.ItemsSource=parties; PartyBox.SelectedIndex=parties.Count>0?0:-1;
        PayerPartyBox.ItemsSource=allParties; RecipientPartyBox.ItemsSource=allParties;
        var readOnlyParties=!context.User.CanManageWarehousesAndParties;
        RecipientNameBox.IsReadOnly=RecipientAddressBox.IsReadOnly=RecipientUnpBox.IsReadOnly=readOnlyParties;
        PayerNameBox.IsReadOnly=PayerAddressBox.IsReadOnly=PayerUnpBox.IsReadOnly=readOnlyParties;
        ItemsGrid.ItemsSource=_items;
        InvoiceTab.Visibility=kind==OperationKind.Shipment?Visibility.Visible:Visibility.Collapsed;
        var saleVisibility=kind==OperationKind.Shipment?Visibility.Visible:Visibility.Collapsed;
        PriceColumn.Visibility=DiscountColumn.Visibility=VatColumn.Visibility=PlacesColumn.Visibility=MassColumn.Visibility=saleVisibility;
        if (kind==OperationKind.Shipment)
        {
            var org=context.Repository.GetOrganization();
            SenderNameBox.Text=org.Name; SenderAddressBox.Text=org.Address; SenderUnpBox.Text=org.Unp;
            InvoiceDate.SelectedDate=context.Repository.GetServerTime().Date;
            AutoNumberBox.IsChecked=true; NumberBox.IsReadOnly=true;
            NumberBox.Text=context.Repository.GetSuggestedInvoiceNumber().ToString(CultureInfo.InvariantCulture);
            PriceModeBox.SelectedIndex=-1; SaveButton.Content="Провести и создать ТТН";
        }
        _ready=true;
        if (kind==OperationKind.Shipment) { FillRecipient(); FillLoading(); }
    }
    private void From_Changed(object sender,SelectionChangedEventArgs e) { if (_ready && _kind==OperationKind.Shipment) FillLoading(); }
    private void FillLoading() { if (FromBox.SelectedItem is WarehouseEntity warehouse) LoadingBox.Text=warehouse.Address; }
    private void Party_Changed(object sender,SelectionChangedEventArgs e) { if (_ready && _kind==OperationKind.Shipment) FillRecipient(); }
    private void FillRecipient()
    {
        if (PartyBox.SelectedItem is not Counterparty p) return;
        RecipientPartyBox.SelectedItem=p;
        SetRecipient(p);
    }
    private void RecipientParty_Changed(object sender,SelectionChangedEventArgs e)
    { if (_ready && RecipientPartyBox.SelectedItem is Counterparty p) SetRecipient(p); }
    private void SetRecipient(Counterparty p)
    {
        RecipientNameBox.Text=p.Name; RecipientAddressBox.Text=p.Address; RecipientUnpBox.Text=p.Unp; UnloadingBox.Text=p.Address;
        if (_payerFollowsRecipient) SetPayer(p.Name,p.Address,p.Unp,p.Id,false);
    }
    private void AutoNumber_Click(object sender,RoutedEventArgs e)
    {
        NumberBox.IsReadOnly=AutoNumberBox.IsChecked==true;
        if (AutoNumberBox.IsChecked==true) Ui.Run(()=>NumberBox.Text=_context.Repository.GetSuggestedInvoiceNumber().ToString(CultureInfo.InvariantCulture));
    }
    private void SetPayer(string name,string address,string unp,int? partyId,bool isSender)
    { PayerNameBox.Text=name; PayerAddressBox.Text=address; PayerUnpBox.Text=unp; _payerPartyId=partyId; _payerIsSender=isSender; }
    private void PayerRecipient_Click(object sender,RoutedEventArgs e)
    { PayerPartyBox.SelectedItem=null; _payerFollowsRecipient=true; SetPayer(RecipientNameBox.Text,RecipientAddressBox.Text,RecipientUnpBox.Text,(RecipientPartyBox.SelectedItem as Counterparty)?.Id,false); }
    private void PayerSender_Click(object sender,RoutedEventArgs e)
    { PayerPartyBox.SelectedItem=null; _payerFollowsRecipient=false; SetPayer(SenderNameBox.Text,SenderAddressBox.Text,SenderUnpBox.Text,null,true); }
    private void PayerClear_Click(object sender,RoutedEventArgs e)
    { PayerPartyBox.SelectedItem=null; _payerFollowsRecipient=false; SetPayer("","","",null,false); }
    private void PayerParty_Changed(object sender,SelectionChangedEventArgs e)
    { if (_ready && PayerPartyBox.SelectedItem is Counterparty p) { _payerFollowsRecipient=false; SetPayer(p.Name,p.Address,p.Unp,p.Id,false); } }
    private void PriceMode_Changed(object sender,SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        var withoutVat=PriceModeBox.SelectedIndex==2;
        TransportVatBox.IsEnabled=!withoutVat;
        if (!withoutVat) return;
        TransportVatBox.Clear();
        for (var i=0;i<_items.Count;i++) _items[i]=_items[i] with { Input=_items[i].Input with { VatRate=null } };
    }
    private void AddItem_Click(object sender,RoutedEventArgs e) => Ui.Run(() =>
    {
        var dialog=new OperationItemWindow(_products,_kind,withoutVat:PriceModeBox.SelectedIndex==2) {Owner=this};
        if (dialog.ShowDialog()==true && dialog.Result is not null) { _items.Add(dialog.Result); ItemsGrid.SelectedItem=dialog.Result; }
    });
    private void EditItem_Click(object sender,RoutedEventArgs e) => Ui.Run(() =>
    {
        if (ItemsGrid.SelectedItem is not OperationItemView item) { Ui.Error("Выберите позицию."); return; }
        var dialog=new OperationItemWindow(_products,_kind,item,PriceModeBox.SelectedIndex==2) {Owner=this};
        if (dialog.ShowDialog()==true && dialog.Result is not null)
        { var index=_items.IndexOf(item); _items[index]=dialog.Result; ItemsGrid.SelectedItem=dialog.Result; }
    });
    private void DeleteItem_Click(object sender,RoutedEventArgs e)
    { if (ItemsGrid.SelectedItem is OperationItemView item) _items.Remove(item); else Ui.Error("Выберите позицию."); }

    private void Save_Click(object sender,RoutedEventArgs e)
    {
        BatchCreationResult? result=null;
        var saved=Ui.Run(() =>
        {
            var from=_kind==OperationKind.Receipt?null:(FromBox.SelectedItem as WarehouseEntity)?.Id;
            var to=_kind==OperationKind.Shipment?null:(ToBox.SelectedItem as WarehouseEntity)?.Id;
            var party=_kind==OperationKind.Transfer?null:(PartyBox.SelectedItem as Counterparty)?.Id;
            InvoiceDraft? invoice=null;
            if (_kind==OperationKind.Shipment)
            {
                if (PriceModeBox.SelectedIndex<0) throw new ArgumentException("На вкладке «Накладная ТТН» выберите режим НДС для реализации.");
                var date=Ui.ReadDate(InvoiceDate,"Дата накладной") ?? throw new ArgumentException("Выберите дату накладной.");
                invoice=new InvoiceDraft { Series=SeriesBox.Text,Number=NumberBox.Text,UseAutomaticNumber=AutoNumberBox.IsChecked==true,Date=date,
                    PriceMode=PriceModeBox.SelectedIndex switch {0=>VatPriceMode.Included,1=>VatPriceMode.Excluded,_=>VatPriceMode.WithoutVat},
                    ExpectedSender=new PartyDetails {Name=SenderNameBox.Text,Address=SenderAddressBox.Text,Unp=SenderUnpBox.Text},
                    Recipient=new PartyDetails {Name=RecipientNameBox.Text,Address=RecipientAddressBox.Text,Unp=RecipientUnpBox.Text}, RecipientCounterpartyId=(RecipientPartyBox.SelectedItem as Counterparty)?.Id,
                    Payer=new PartyDetails {Name=PayerNameBox.Text,Address=PayerAddressBox.Text,Unp=PayerUnpBox.Text}, PayerCounterpartyId=_payerPartyId,PayerIsSender=_payerIsSender,
                    BasisName=BasisNameBox.Text,BasisNumber=BasisNumberBox.Text,BasisDate=Ui.ReadDate(BasisDate,"Дата основания"),
                    LoadingPoint=LoadingBox.Text,UnloadingPoint=UnloadingBox.Text,Vehicle=VehicleBox.Text,Trailer=TrailerBox.Text,
                    Driver=DriverBox.Text,WaybillNumber=WaybillBox.Text,
                    TransportPrice=Ui.ParseOptionalNumber(TransportPriceBox.Text,"Стоимость транспортных услуг"),
                    TransportVatRate=Ui.ParseOptionalNumber(TransportVatBox.Text,"НДС транспортных услуг") };
            }
            result=_context.Repository.CreateBatch(new BatchOperationInput(_kind,from,to,party,CommentBox.Text,_items.Select(x=>x.Input).ToArray(),invoice),_context.User);
        });
        if (!saved || result is null) return;
        // После коммита никакая ошибка интерфейса/файла не должна выглядеть как неуспешная продажа.
        try { _context.NotifyDataChanged(); } catch { }
        string? file=null; string? exportError=null;
        if (result.Invoice is not null)
        {
            try { file=InvoiceFiles.SaveAutomatic(result.Invoice,_context.Repository); }
            catch (Exception ex) { exportError=ex.Message; }
        }
        if (exportError is not null)
            MessageBox.Show($"Операция Г-{result.BatchId} проведена, накладная сохранена в базе, но DOCX не создан: {exportError}\nПовторно проводить операцию не нужно: сохраните накладную из списка операций.","Операция проведена",MessageBoxButton.OK,MessageBoxImage.Warning);
        else if (file is not null)
        {
            var invoiceLabel=(string.IsNullOrWhiteSpace(result.Invoice!.Series) ? "" : result.Invoice.Series+" ")+"№"+result.Invoice.Number;
            var answer=MessageBox.Show($"Операция Г-{result.BatchId} проведена. Накладная {invoiceLabel} создана:\n{file}\n\nОткрыть документ?","Операция проведена",MessageBoxButton.YesNo,MessageBoxImage.Information);
            if (answer==MessageBoxResult.Yes) { try { InvoiceFiles.Open(file); } catch(Exception ex) { Ui.Error("Файл создан, но открыть его не удалось: "+ex.Message); } }
        }
        else MessageBox.Show($"Операция Г-{result.BatchId} проведена.","Склад",MessageBoxButton.OK,MessageBoxImage.Information);
        DialogResult=true;
    }
}
