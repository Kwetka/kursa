using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using kursa.Infrastructure;
using kursa.Views.Dialogs;
using Warehouse.Core.Models;

namespace kursa.Views;
public partial class ProductsView : UserControl
{
    private readonly WorkspaceContext _context;
    private readonly FilterIndicator _filters;
    private bool _loaded;
    public ProductsView(WorkspaceContext context)
    {
        InitializeComponent(); _context=context; ActionsPanel.IsEnabled=context.User.CanManageProducts;
        _filters=new FilterIndicator(ApplyButton,FiltersExpander,()=>[SearchBox.Text]); _filters.Track(SearchBox);
    }
    public void Refresh() { if (!_loaded || !_filters.IsDirty) LoadAndApply(); }
    private void LoadAndApply() => Ui.Run(() =>
    {
        var items=_context.Repository.GetProducts(); var term=SearchBox.Text.Trim();
        var filtered=items.Where(x=>x.Name.Contains(term,StringComparison.CurrentCultureIgnoreCase) || x.Unit.Contains(term,StringComparison.CurrentCultureIgnoreCase) || x.Id.ToString().Contains(term)).ToList();
        GridView.ItemsSource=filtered; CountText.Text=$"Показано товаров: {filtered.Count}"; _loaded=true; _filters.MarkApplied();
    });
    private void Apply_Click(object sender,RoutedEventArgs e) => LoadAndApply();
    private void Reset_Click(object sender,RoutedEventArgs e) { SearchBox.Text=""; LoadAndApply(); }
    private void Search_KeyDown(object sender,KeyEventArgs e) { if (e.Key==Key.Enter) { LoadAndApply(); e.Handled=true; } }
    private void Add_Click(object sender,RoutedEventArgs e) => Ui.Run(() => { if (new ProductWindow(_context,null) {Owner=Window.GetWindow(this)}.ShowDialog()==true) Refresh(); });
    private void Edit_Click(object sender,RoutedEventArgs e)
    {
        if (!_context.User.CanManageProducts) return;
        Ui.Run(() =>
        {
            if (GridView.SelectedItem is not Product item) { Ui.Error("Выберите товар."); return; }
            if (new ProductWindow(_context,item) {Owner=Window.GetWindow(this)}.ShowDialog()==true) Refresh();
        });
    }
    private void Delete_Click(object sender,RoutedEventArgs e) => Ui.Run(() =>
    {
        if (GridView.SelectedItem is not Product item) { Ui.Error("Выберите товар."); return; }
        if (!Ui.Confirm($"Удалить товар «{item.Name}»? Товары с историей удалить нельзя.")) return;
        _context.Repository.DeleteProduct(item.Id,_context.User); _context.NotifyDataChanged(); Refresh();
    });
}
