using System.Windows;
using System.Windows.Controls;
using kursa.Infrastructure;
using kursa.Views.Dialogs;
using WarehouseEntity = Warehouse.Core.Models.Warehouse;

namespace kursa.Views;
public partial class WarehousesView : UserControl
{
    private readonly WorkspaceContext _context;
    private List<WarehouseEntity> _items=[];
    public WarehousesView(WorkspaceContext context) { InitializeComponent(); _context=context; }
    public void Refresh() => Ui.Run(() => { _items=_context.Repository.GetWarehouses(); ApplySearch(); });
    private void ApplySearch()
    {
        if (GridView is null || SearchBox is null) return;
        var term=SearchBox.Text.Trim();
        var filtered=_items.Where(x=>x.Name.Contains(term,StringComparison.CurrentCultureIgnoreCase) || x.Address.Contains(term,StringComparison.CurrentCultureIgnoreCase) || x.ManagerName.Contains(term,StringComparison.CurrentCultureIgnoreCase) || x.ManagerContact.Contains(term,StringComparison.CurrentCultureIgnoreCase)).ToList();
        GridView.ItemsSource=filtered; CountText.Text=$"Показано складов: {filtered.Count}";
    }
    private void Search_Changed(object sender, TextChangedEventArgs e) => ApplySearch();
    private void Add_Click(object sender, RoutedEventArgs e) => Ui.Run(() => { if (new WarehouseWindow(_context,null) {Owner=Window.GetWindow(this)}.ShowDialog()==true) Refresh(); });
    private void Edit_Click(object sender, RoutedEventArgs e) => Ui.Run(() =>
    {
        if (GridView.SelectedItem is not WarehouseEntity item) { Ui.Error("Выберите склад."); return; }
        if (new WarehouseWindow(_context,item) {Owner=Window.GetWindow(this)}.ShowDialog()==true) Refresh();
    });
    private void Delete_Click(object sender, RoutedEventArgs e) => Ui.Run(() =>
    {
        if (GridView.SelectedItem is not WarehouseEntity item) { Ui.Error("Выберите склад."); return; }
        if (!Ui.Confirm($"Удалить склад «{item.Name}»? Склады с историей удалить нельзя.")) return;
        _context.Repository.DeleteWarehouse(item.Id,_context.User); _context.NotifyDataChanged(); Refresh();
    });
}
