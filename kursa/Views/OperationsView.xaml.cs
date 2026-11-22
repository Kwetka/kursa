using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using kursa.Infrastructure;
using kursa.Views.Dialogs;
using Warehouse.Core.Models;

namespace kursa.Views;
public partial class OperationsView : UserControl
{
    private readonly WorkspaceContext _context;
    public OperationsView(WorkspaceContext context) { InitializeComponent(); _context=context; }
    public void Refresh() => Ui.Run(() =>
    {
        var product=Ui.SelectedId(ProductBox); var warehouse=Ui.SelectedId(WarehouseBox); var party=Ui.SelectedId(PartyBox); var kind=Ui.SelectedId(KindBox);
        Ui.SetLookup(ProductBox, new[] {new LookupItem(null,"Все товары")}.Concat(_context.Repository.GetProducts().Select(p=>new LookupItem(p.Id,p.Name))),product);
        Ui.SetLookup(WarehouseBox, new[] {new LookupItem(null,"Все склады")}.Concat(_context.Repository.GetWarehouses().Select(w=>new LookupItem(w.Id,w.Name))),warehouse);
        Ui.SetLookup(PartyBox, new[] {new LookupItem(null,"Все контрагенты")}.Concat(_context.Repository.GetCounterparties().Select(p=>new LookupItem(p.Id,p.Name))),party);
        Ui.SetLookup(KindBox,[new(null,"Все операции"),new((int)OperationKind.Receipt,"Приход"),new((int)OperationKind.Shipment,"Расход"),new((int)OperationKind.Transfer,"Перемещение")],kind);
        Apply();
    });
    private void Apply()
    {
        GridView.ItemsSource=null; CountText.Text="Результаты не загружены.";
        Ui.Run(() =>
        {
            var type=Ui.SelectedId(KindBox);
            var interval=Ui.BuildInterval(Ui.ReadDate(FromPicker,"Дата с"),FromTimeBox.Text,Ui.ReadDate(ToPicker,"Дата по"),ToTimeBox.Text);
            var rows=_context.Repository.GetOperations(new OperationFilter { FromInclusive=interval.From,ToExclusive=interval.ToExclusive,
                ProductId=Ui.SelectedId(ProductBox),WarehouseId=Ui.SelectedId(WarehouseBox),CounterpartyId=Ui.SelectedId(PartyBox),
                Kind=type.HasValue ? (OperationKind)type.Value : null,Search=SearchBox.Text });
            GridView.ItemsSource=rows; CountText.Text=$"Найдено операций: {rows.Count}. Пустое время: начало / конец дня; указанная конечная минута включена.";
        });
    }
    private void Apply_Click(object sender, RoutedEventArgs e) => Apply();
    private void Search_KeyDown(object sender, KeyEventArgs e) { if (e.Key==Key.Enter) { Apply(); e.Handled=true; } }
    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        FromPicker.SelectedDate=null; ToPicker.SelectedDate=null; ProductBox.SelectedIndex=WarehouseBox.SelectedIndex=PartyBox.SelectedIndex=KindBox.SelectedIndex=0;
        FromTimeBox.Text=ToTimeBox.Text=SearchBox.Text=""; Apply();
    }
    private void OpenOperation(OperationKind kind) => Ui.Run(() =>
    {
        if (new OperationWindow(_context,kind) {Owner=Window.GetWindow(this)}.ShowDialog()==true) Refresh();
    });
    private void Receipt_Click(object sender, RoutedEventArgs e) => OpenOperation(OperationKind.Receipt);
    private void Shipment_Click(object sender, RoutedEventArgs e) => OpenOperation(OperationKind.Shipment);
    private void Transfer_Click(object sender, RoutedEventArgs e) => OpenOperation(OperationKind.Transfer);
}
