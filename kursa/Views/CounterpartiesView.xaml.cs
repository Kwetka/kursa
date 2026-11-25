using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using kursa.Infrastructure;
using kursa.Views.Dialogs;
using Warehouse.Core.Models;

namespace kursa.Views;
public partial class CounterpartiesView : UserControl
{
    private readonly WorkspaceContext _context;
    private readonly FilterIndicator _filters;
    private bool _loaded;
    public CounterpartiesView(WorkspaceContext context)
    {
        InitializeComponent(); _context=context; ActionsPanel.IsEnabled=context.User.CanManageAnyCounterparty;
        _filters=new FilterIndicator(ApplyButton,FiltersExpander,()=>[SearchBox.Text]); _filters.Track(SearchBox);
    }
    public void Refresh() { if (!_loaded || !_filters.IsDirty) LoadAndApply(); }
    private void LoadAndApply() => Ui.Run(() =>
    {
        var items=_context.Repository.GetCounterparties(); var term=SearchBox.Text.Trim();
        var filtered=items.Where(x=>x.Name.Contains(term,StringComparison.CurrentCultureIgnoreCase) || x.TypeName.Contains(term,StringComparison.CurrentCultureIgnoreCase)
            || x.Unp.Contains(term,StringComparison.CurrentCultureIgnoreCase) || x.Address.Contains(term,StringComparison.CurrentCultureIgnoreCase)).ToList();
        GridView.ItemsSource=filtered; CountText.Text=$"Показано контрагентов: {filtered.Count}"; _loaded=true; _filters.MarkApplied();
    });
    private void Apply_Click(object sender,RoutedEventArgs e) => LoadAndApply();
    private void Reset_Click(object sender,RoutedEventArgs e) { SearchBox.Text=""; LoadAndApply(); }
    private void Search_KeyDown(object sender,KeyEventArgs e) { if (e.Key==Key.Enter) { LoadAndApply(); e.Handled=true; } }
    private void Add_Click(object sender,RoutedEventArgs e) => Ui.Run(() => { if (new CounterpartyWindow(_context,null) {Owner=Window.GetWindow(this)}.ShowDialog()==true) Refresh(); });
    private void Edit_Click(object sender,RoutedEventArgs e)
    {
        if (!_context.User.CanManageAnyCounterparty) return;
        Ui.Run(() =>
        {
            if (GridView.SelectedItem is not Counterparty item) { Ui.Error("Выберите контрагента."); return; }
            if (!_context.User.CanManageCounterparty(item.IsSupplier))
            { Ui.Error(item.IsSupplier ? "Поставщиков ведут кладовщик и администратор." : "Покупателей ведут менеджер продаж и администратор."); return; }
            if (new CounterpartyWindow(_context,item) {Owner=Window.GetWindow(this)}.ShowDialog()==true) Refresh();
        });
    }
    private void Delete_Click(object sender,RoutedEventArgs e) => Ui.Run(() =>
    {
        if (GridView.SelectedItem is not Counterparty item) { Ui.Error("Выберите контрагента."); return; }
        if (!Ui.Confirm($"Удалить контрагента «{item.Name}»? Используемые записи удалить нельзя.")) return;
        _context.Repository.DeleteCounterparty(item.Id,_context.User); _context.NotifyDataChanged(); Refresh();
    });
}
