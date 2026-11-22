using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using kursa.Infrastructure;
using kursa.Views.Dialogs;
using WarehouseEntity = Warehouse.Core.Models.Warehouse;

namespace kursa.Views;
public partial class WarehousesView : UserControl
{
    private readonly WorkspaceContext _context;
    private readonly FilterIndicator _filters;
    private bool _loaded;
    public WarehousesView(WorkspaceContext context)
    {
        InitializeComponent(); _context=context; ActionsPanel.IsEnabled=context.User.CanManageWarehousesAndParties;
        _filters=new FilterIndicator(ApplyButton,FiltersExpander,()=>[SearchBox.Text]); _filters.Track(SearchBox);
    }
    public void Refresh() { if (!_loaded || !_filters.IsDirty) LoadAndApply(); }
    private void LoadAndApply() => Ui.Run(() =>
    {
        var items=_context.Repository.GetWarehouses(); var term=SearchBox.Text.Trim();
        var filtered=items.Where(x=>x.Name.Contains(term,StringComparison.CurrentCultureIgnoreCase) || x.Address.Contains(term,StringComparison.CurrentCultureIgnoreCase) || x.ManagerName.Contains(term,StringComparison.CurrentCultureIgnoreCase) || x.ManagerContact.Contains(term,StringComparison.CurrentCultureIgnoreCase)).ToList();
        GridView.ItemsSource=filtered; CountText.Text=$"Показано складов: {filtered.Count}"; _loaded=true; _filters.MarkApplied();
    });
    private void Apply_Click(object sender,RoutedEventArgs e) => LoadAndApply();
    private void Reset_Click(object sender,RoutedEventArgs e) { SearchBox.Text=""; LoadAndApply(); }
    private void Search_KeyDown(object sender,KeyEventArgs e) { if (e.Key==Key.Enter) { LoadAndApply(); e.Handled=true; } }
    private void Add_Click(object sender,RoutedEventArgs e) => Ui.Run(() => { if (new WarehouseWindow(_context,null) {Owner=Window.GetWindow(this)}.ShowDialog()==true) Refresh(); });
    private void Edit_Click(object sender,RoutedEventArgs e)
    {
        if (!_context.User.CanManageWarehousesAndParties) return;
        Ui.Run(() =>
        {
            if (GridView.SelectedItem is not WarehouseEntity item) { Ui.Error("Выберите склад."); return; }
            if (new WarehouseWindow(_context,item) {Owner=Window.GetWindow(this)}.ShowDialog()==true) Refresh();
        });
    }
    private void Delete_Click(object sender,RoutedEventArgs e) => Ui.Run(() =>
    {
        if (GridView.SelectedItem is not WarehouseEntity item) { Ui.Error("Выберите склад."); return; }
        if (!Ui.Confirm($"Удалить склад «{item.Name}»? Склады с историей удалить нельзя.")) return;
        _context.Repository.DeleteWarehouse(item.Id,_context.User); _context.NotifyDataChanged(); Refresh();
    });
}
