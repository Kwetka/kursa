using System.Windows;
using System.Windows.Controls;
using kursa.Infrastructure;
using kursa.Views.Dialogs;
using Warehouse.Core.Models;

namespace kursa.Views;
public partial class CounterpartiesView : UserControl
{
    private readonly WorkspaceContext _context;
    private List<Counterparty> _items=[];
    public CounterpartiesView(WorkspaceContext context) { InitializeComponent(); _context=context; }
    public void Refresh() => Ui.Run(() => { _items=_context.Repository.GetCounterparties(); ApplySearch(); });
    private void ApplySearch()
    {
        if (GridView is null || SearchBox is null) return;
        var term=SearchBox.Text.Trim();
        var filtered=_items.Where(x=>x.Name.Contains(term,StringComparison.CurrentCultureIgnoreCase) || x.TypeName.Contains(term,StringComparison.CurrentCultureIgnoreCase)).ToList();
        GridView.ItemsSource=filtered; CountText.Text=$"Показано контрагентов: {filtered.Count}";
    }
    private void Search_Changed(object sender, TextChangedEventArgs e) => ApplySearch();
    private void Add_Click(object sender, RoutedEventArgs e) => Ui.Run(() => { if (new CounterpartyWindow(_context,null) {Owner=Window.GetWindow(this)}.ShowDialog()==true) Refresh(); });
    private void Edit_Click(object sender, RoutedEventArgs e) => Ui.Run(() =>
    {
        if (GridView.SelectedItem is not Counterparty item) { Ui.Error("Выберите контрагента."); return; }
        if (new CounterpartyWindow(_context,item) {Owner=Window.GetWindow(this)}.ShowDialog()==true) Refresh();
    });
    private void Delete_Click(object sender, RoutedEventArgs e) => Ui.Run(() =>
    {
        if (GridView.SelectedItem is not Counterparty item) { Ui.Error("Выберите контрагента."); return; }
        if (!Ui.Confirm($"Удалить контрагента «{item.Name}»? Используемые записи удалить нельзя.")) return;
        _context.Repository.DeleteCounterparty(item.Id,_context.User); _context.NotifyDataChanged(); Refresh();
    });
}
