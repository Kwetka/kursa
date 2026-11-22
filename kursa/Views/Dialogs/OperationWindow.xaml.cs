using System.Windows;
using kursa.Infrastructure;
using Warehouse.Core.Models;
using WarehouseEntity = Warehouse.Core.Models.Warehouse;

namespace kursa.Views.Dialogs;
public partial class OperationWindow : Window
{
    private readonly WorkspaceContext _context;
    private readonly OperationKind _kind;
    public OperationWindow(WorkspaceContext context, OperationKind kind)
    {
        InitializeComponent(); _context=context; _kind=kind;
        Heading.Text=kind switch { OperationKind.Receipt=>"Поступление товара", OperationKind.Shipment=>"Расход товара", _=>"Перемещение товара" };
        var products=context.Repository.GetProducts(); var warehouses=context.Repository.GetWarehouses();
        ProductBox.ItemsSource=products; ProductBox.SelectedIndex=products.Count>0 ? 0 : -1;
        FromBox.ItemsSource=warehouses; ToBox.ItemsSource=warehouses;
        FromBox.SelectedIndex=warehouses.Count>0 ? 0 : -1;
        ToBox.SelectedIndex=warehouses.Count>0 ? (kind==OperationKind.Transfer && warehouses.Count>1 ? 1 : 0) : -1;
        FromPanel.Visibility=kind==OperationKind.Receipt ? Visibility.Collapsed : Visibility.Visible;
        ToPanel.Visibility=kind==OperationKind.Shipment ? Visibility.Collapsed : Visibility.Visible;
        PartyPanel.Visibility=kind==OperationKind.Transfer ? Visibility.Collapsed : Visibility.Visible;
        PartyLabel.Text=kind==OperationKind.Receipt ? "Поставщик" : "Покупатель";
        var parties=context.Repository.GetCounterparties().Where(x=>x.IsSupplier==(kind==OperationKind.Receipt)).ToList();
        PartyBox.ItemsSource=parties; PartyBox.SelectedIndex=parties.Count>0 ? 0 : -1;
    }
    private void Save_Click(object sender, RoutedEventArgs e) => Ui.Run(() =>
    {
        var product=(ProductBox.SelectedItem as Product)?.Id ?? 0;
        var from=_kind==OperationKind.Receipt ? null : (FromBox.SelectedItem as WarehouseEntity)?.Id;
        var to=_kind==OperationKind.Shipment ? null : (ToBox.SelectedItem as WarehouseEntity)?.Id;
        var party=_kind==OperationKind.Transfer ? null : (PartyBox.SelectedItem as Counterparty)?.Id;
        var id=_context.Repository.CreateOperation(new OperationInput(_kind,product,Ui.ParseNumber(QuantityBox.Text,"Количество"),from,to,party,CommentBox.Text),_context.User);
        _context.NotifyDataChanged();
        MessageBox.Show($"Операция №{id} проведена.","Склад",MessageBoxButton.OK,MessageBoxImage.Information);
        DialogResult=true;
    });
}
