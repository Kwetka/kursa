using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using kursa.Infrastructure;
using Warehouse.Core.Models;

namespace kursa.Views;
public partial class AuditView : UserControl
{
    private readonly WorkspaceContext _context;
    private readonly FilterIndicator _filters;
    private bool _loaded;
    public AuditView(WorkspaceContext context)
    {
        InitializeComponent(); _context=context;
        _filters=new FilterIndicator(ApplyButton,FiltersExpander,()=>[SearchBox.Text]); _filters.Track(SearchBox);
        ShowStatus(context.Repository.AuditStatus);
    }
    public void Refresh()
    {
        ShowStatus(_context.Repository.SyncAuditFile());
        if (!_loaded || !_filters.IsDirty) LoadAndApply();
    }
    public void ShowStatus(AuditFileStatus status)
    {
        StatusText.Text=status.Message; PathBox.Text=status.Path;
        StatusText.Foreground=status.State switch { AuditFileState.Verified=>Brushes.DarkGreen, AuditFileState.Diverged=>Brushes.DarkRed, _=>Brushes.DarkOrange };
    }
    private void LoadAndApply() => Ui.Run(() =>
    {
        var items=_context.Repository.GetAuditEntries(); var term=SearchBox.Text.Trim();
        var rows=items.Where(x=>x.ActorLogin.Contains(term,StringComparison.CurrentCultureIgnoreCase) || x.ActorName.Contains(term,StringComparison.CurrentCultureIgnoreCase)
            || x.ActorRole.Contains(term,StringComparison.CurrentCultureIgnoreCase) || x.Action.Contains(term,StringComparison.CurrentCultureIgnoreCase)
            || x.Entity.Contains(term,StringComparison.CurrentCultureIgnoreCase) || x.Details.Contains(term,StringComparison.CurrentCultureIgnoreCase)
            || x.Id.ToString().Contains(term)).ToList();
        GridView.ItemsSource=rows; CountText.Text=$"Показано: {rows.Count} из последних {items.Count} SQL-событий. Текущий поток файла: {_context.Repository.AuditStreamId}.";
        _loaded=true; _filters.MarkApplied();
        if (rows.Count>0) GridView.SelectedIndex=0; else DetailsBox.Text="";
    });
    private void Verify_Click(object sender,RoutedEventArgs e) => Refresh();
    private void Apply_Click(object sender,RoutedEventArgs e) => LoadAndApply();
    private void Reset_Click(object sender,RoutedEventArgs e) { SearchBox.Text=""; LoadAndApply(); }
    private void Search_KeyDown(object sender,KeyEventArgs e) { if (e.Key==Key.Enter) { LoadAndApply(); e.Handled=true; } }
    private void Grid_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if (DetailsBox is not null) DetailsBox.Text=GridView.SelectedItem is AuditEntry entry ? AuditFormatter.Details(entry.Details) : "";
    }
}
