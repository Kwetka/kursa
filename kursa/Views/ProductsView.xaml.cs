using System.Windows;
using System.Windows.Controls;
using kursa.Infrastructure;
using kursa.Views.Dialogs;
using Warehouse.Core.Models;

namespace kursa.Views;
public partial class ProductsView : UserControl
{
    private readonly WorkspaceContext _context;
    private List<Product> _items=[];
    public ProductsView(WorkspaceContext context) { InitializeComponent(); _context=context; }
    public void Refresh() => Ui.Run(() => { _items=_context.Repository.GetProducts(); ApplySearch(); });
    private void ApplySearch()
    {
        if (GridView is null || SearchBox is null) return;
        var term=SearchBox.Text.Trim();
        var filtered=_items.Where(x=>x.Name.Contains(term,StringComparison.CurrentCultureIgnoreCase) || x.Unit.Contains(term,StringComparison.CurrentCultureIgnoreCase) || x.Id.ToString().Contains(term)).ToList();
        GridView.ItemsSource=filtered; CountText.Text=$"Показано товаров: {filtered.Count}";
    }
    private void Search_Changed(object sender, TextChangedEventArgs e) => ApplySearch();
    private void Add_Click(object sender, RoutedEventArgs e) => Ui.Run(() => { if (new ProductWindow(_context,null) {Owner=Window.GetWindow(this)}.ShowDialog()==true) Refresh(); });
    private void Edit_Click(object sender, RoutedEventArgs e) => Ui.Run(() =>
    {
        if (GridView.SelectedItem is not Product item) { Ui.Error("Выберите товар."); return; }
        if (new ProductWindow(_context,item) {Owner=Window.GetWindow(this)}.ShowDialog()==true) Refresh();
    });
    private void Delete_Click(object sender, RoutedEventArgs e) => Ui.Run(() =>
    {
        if (GridView.SelectedItem is not Product item) { Ui.Error("Выберите товар."); return; }
        if (!Ui.Confirm($"Удалить товар «{item.Name}»? Товары с историей удалить нельзя.")) return;
        _context.Repository.DeleteProduct(item.Id,_context.User); _context.NotifyDataChanged(); Refresh();
    });
}
