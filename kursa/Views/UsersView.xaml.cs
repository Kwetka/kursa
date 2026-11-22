using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using kursa.Infrastructure;
using kursa.Views.Dialogs;
using Warehouse.Core.Models;

namespace kursa.Views;
public partial class UsersView : UserControl
{
    private readonly WorkspaceContext _context;
    private readonly FilterIndicator _filters;
    private bool _loaded;
    public UsersView(WorkspaceContext context)
    {
        InitializeComponent(); _context=context;
        _filters=new FilterIndicator(ApplyButton,FiltersExpander,()=>[SearchBox.Text]); _filters.Track(SearchBox);
    }
    public void Refresh() { if (!_loaded || !_filters.IsDirty) LoadAndApply(); }
    private void LoadAndApply() => Ui.Run(() =>
    {
        var items=_context.Repository.GetUsers(_context.User); var term=SearchBox.Text.Trim();
        var filtered=items.Where(x=>x.Login.Contains(term,StringComparison.CurrentCultureIgnoreCase) || x.FullName.Contains(term,StringComparison.CurrentCultureIgnoreCase) || x.RoleName.Contains(term,StringComparison.CurrentCultureIgnoreCase)).ToList();
        GridView.ItemsSource=filtered; CountText.Text=$"Активных учётных записей: {filtered.Count}"; _loaded=true; _filters.MarkApplied();
    });
    private void Apply_Click(object sender,RoutedEventArgs e) => LoadAndApply();
    private void Reset_Click(object sender,RoutedEventArgs e) { SearchBox.Text=""; LoadAndApply(); }
    private void Search_KeyDown(object sender,KeyEventArgs e) { if (e.Key==Key.Enter) { LoadAndApply(); e.Handled=true; } }
    private void Add_Click(object sender,RoutedEventArgs e) => Ui.Run(() => { if (new UserWindow(_context,null) {Owner=Window.GetWindow(this)}.ShowDialog()==true) Refresh(); });
    private void Edit_Click(object sender,RoutedEventArgs e) => Ui.Run(() =>
    {
        if (GridView.SelectedItem is not UserAccount item) { Ui.Error("Выберите пользователя."); return; }
        if (new UserWindow(_context,item) {Owner=Window.GetWindow(this)}.ShowDialog()==true) Refresh();
    });
    private void Delete_Click(object sender,RoutedEventArgs e) => Ui.Run(() =>
    {
        if (GridView.SelectedItem is not UserAccount item) { Ui.Error("Выберите пользователя."); return; }
        if (!Ui.Confirm($"Удалить учётную запись «{item.Login}»? Вход будет запрещён, история операций сохранится.")) return;
        _context.Repository.DeleteUser(item.Id,_context.User); _context.NotifyDataChanged(); Refresh();
    });
}
