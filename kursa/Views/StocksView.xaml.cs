using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using kursa.Infrastructure;
using kursa.Views.Dialogs;
using Warehouse.Core.Models;

namespace kursa.Views;
public partial class StocksView : UserControl
{
    private readonly WorkspaceContext _context;
    private readonly FilterIndicator _filters;
    private bool _loaded;
    private IReadOnlyList<StockRow>? _appliedRows;
    private string _appliedSummary="";
    public event EventHandler? ResultsChanged;
    // Сохраняем старую таблицу на экране, но не позволяем строить отчёт по неприменённым полям.
    public IReadOnlyList<StockRow>? LastRows { get; private set; }
    public string AppliedSummary { get; private set; } = "";
    public StocksView(WorkspaceContext context)
    {
        InitializeComponent(); _context=context;
        _filters=new FilterIndicator(ApplyButton,FiltersExpander,()=>[FromPicker.Text,FromTimeBox.Text,ToPicker.Text,ToTimeBox.Text,
            Ui.SelectedId(ProductBox),Ui.SelectedId(WarehouseBox),MinimumBox.Text,SearchBox.Text,ZeroBox.IsChecked]);
        _filters.Track(FromPicker,FromTimeBox,ToPicker,ToTimeBox,ProductBox,WarehouseBox,MinimumBox,SearchBox,ZeroBox);
        _filters.StateChanged+=(_,_) =>
        {
            LastRows=_filters.IsDirty ? null : _appliedRows; AppliedSummary=_filters.IsDirty ? "" : _appliedSummary;
            ResultsChanged?.Invoke(this,EventArgs.Empty);
        };
    }
    public void Refresh()
    {
        if (_loaded && _filters.IsDirty) return;
        Ui.Run(() =>
        {
            var product=Ui.SelectedId(ProductBox); var warehouse=Ui.SelectedId(WarehouseBox);
            Ui.SetLookup(ProductBox,new[] {new LookupItem(null,"Все товары")}.Concat(_context.Repository.GetProducts().Select(p=>new LookupItem(p.Id,p.Name))),product);
            Ui.SetLookup(WarehouseBox,new[] {new LookupItem(null,"Все склады")}.Concat(_context.Repository.GetWarehouses().Select(w=>new LookupItem(w.Id,w.Name))),warehouse);
            Apply();
        });
    }
    private void Apply() => Ui.Run(() =>
    {
        decimal? min=string.IsNullOrWhiteSpace(MinimumBox.Text) ? null : Ui.ParseNumber(MinimumBox.Text,"Остаток от");
        if (min is < 0 or > 1_000_000_000m || (min.HasValue && decimal.Round(min.Value,3)!=min.Value))
            throw new ArgumentException("Минимальный остаток: от 0 до 1 000 000 000, не более трёх знаков после запятой.");
        var interval=Ui.BuildInterval(Ui.ReadDate(FromPicker,"Дата с"),FromTimeBox.Text,Ui.ReadDate(ToPicker,"Дата по"),ToTimeBox.Text);
        var filter=new StockFilter {LastMovementFromInclusive=interval.From,LastMovementToExclusive=interval.ToExclusive,
            ProductId=Ui.SelectedId(ProductBox),WarehouseId=Ui.SelectedId(WarehouseBox),MinimumQuantity=min,
            IncludeZero=ZeroBox.IsChecked==true,Search=SearchBox.Text};
        var rows=_context.Repository.GetStocks(filter);
        _appliedRows=rows; LastRows=rows; GridView.ItemsSource=rows;
        CountText.Text=$"Показано строк: {rows.Count}   ·   Закупочная стоимость: {rows.Sum(x=>x.PurchaseValue):N2} BYN   ·   Продажная стоимость: {rows.Sum(x=>x.SaleValue):N2} BYN";
        _appliedSummary=$"Склад: {(WarehouseBox.SelectedItem as LookupItem)?.Name}; товар: {(ProductBox.SelectedItem as LookupItem)?.Name}; " +
            $"последнее движение (конец не включается): {filter.LastMovementFromInclusive?.ToString("dd.MM.yyyy HH:mm") ?? "любая дата"} — {filter.LastMovementToExclusive?.ToString("dd.MM.yyyy HH:mm") ?? "любая дата"}; " +
            $"остаток от: {(min.HasValue ? min.Value.ToString("N3") : "не задан")}; поиск: {(string.IsNullOrWhiteSpace(filter.Search) ? "нет" : filter.Search.Trim())}; нулевые: {(filter.IncludeZero ? "да" : "нет")}.";
        AppliedSummary=_appliedSummary; _loaded=true; _filters.MarkApplied(); ResultsChanged?.Invoke(this,EventArgs.Empty);
    });
    private void Apply_Click(object sender,RoutedEventArgs e) => Apply();
    private void Refresh_Click(object sender,RoutedEventArgs e) { if (_filters.IsDirty) Ui.Error("Сначала нажмите «Применить» в фильтрах."); else Refresh(); }
    private void Search_KeyDown(object sender,KeyEventArgs e) { if (e.Key==Key.Enter) { Apply(); e.Handled=true; } }
    private void Reset_Click(object sender,RoutedEventArgs e)
    {
        FromPicker.SelectedDate=null; ToPicker.SelectedDate=null; ProductBox.SelectedIndex=WarehouseBox.SelectedIndex=0;
        MinimumBox.Text=SearchBox.Text=FromTimeBox.Text=ToTimeBox.Text=""; ZeroBox.IsChecked=false; Apply();
    }
    private void History_Click(object sender,RoutedEventArgs e) => Ui.Run(() =>
    {
        if (GridView.SelectedItem is not StockRow row) { if (sender is Button) Ui.Error("Выберите строку остатка."); return; }
        new ProductHistoryWindow(_context,row.ProductId,row.ProductName) {Owner=Window.GetWindow(this)}.Show();
    });
}
