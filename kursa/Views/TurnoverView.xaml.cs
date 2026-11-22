using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using kursa.Infrastructure;
using Warehouse.Core.Models;
using Warehouse.Core.Services;

namespace kursa.Views;
public partial class TurnoverView : UserControl
{
    private readonly WorkspaceContext _context;
    private readonly ObservableCollection<ProductChoice> _choices=[];
    private readonly FilterIndicator _filters;
    private bool _ready, _loading, _initialised;
    private IReadOnlyList<TurnoverRow>? _appliedRows;
    private string _appliedSummary="";
    public event EventHandler? ResultsInvalidated;
    public IReadOnlyList<TurnoverRow>? LastRows { get; private set; }
    public string AppliedSummary { get; private set; } = "";

    public TurnoverView(WorkspaceContext context)
    {
        InitializeComponent(); _context=context; ProductList.ItemsSource=_choices;
        _filters=new FilterIndicator(ApplyButton,FiltersExpander,()=>[FromPicker.Text,FromTimeBox.Text,ToPicker.Text,ToTimeBox.Text,
            Ui.SelectedId(WarehouseBox),string.Join(",",_choices.Where(c=>c.IsSelected).Select(c=>c.Product.Id).Order())]);
        _filters.Track(FromPicker,FromTimeBox,ToPicker,ToTimeBox,WarehouseBox);
        _filters.StateChanged+=(_,_) => UpdateResultState();
        _ready=true;
        context.DataChanged+=(_,_) => { if (_appliedRows is not null) _filters.ForceDirty(); };
    }
    public void Refresh()
    {
        // Обновляем список товаров даже при неприменённых параметрах, сохраняя отметки.
        // Сам расчёт при этом не выполняется и предыдущая таблица остаётся на экране.
        Ui.Run(() =>
        {
            var previous=_choices.ToDictionary(c=>c.Product.Id,c=>c.IsSelected);
            var warehouse=Ui.SelectedId(WarehouseBox);
            var products=_context.Repository.GetProducts(); var warehouses=_context.Repository.GetWarehouses();
            bool first=!_initialised;
            _loading=true;
            try
            {
                foreach(var choice in _choices) choice.PropertyChanged-=Choice_Changed;
                _choices.Clear();
                foreach(var p in products)
                {
                    var choice=new ProductChoice(p,previous.TryGetValue(p.Id,out var selected) ? selected : true);
                    choice.PropertyChanged+=Choice_Changed; _choices.Add(choice);
                }
                Ui.SetLookup(WarehouseBox,new[] {new LookupItem(null,"Все склады")}.Concat(warehouses.Select(w=>new LookupItem(w.Id,w.Name))),warehouse);
                if (first)
                {
                    var today=_context.Repository.GetServerTime().Date;
                    FromPicker.SelectedDate=today.AddDays(-29); ToPicker.SelectedDate=today; _initialised=true;
                }
                ApplyProductSearch(); UpdateSelectionText();
            }
            finally { _loading=false; }
            if (first) _filters.MarkApplied(); else _filters.Update();
            UpdateResultState();
        });
    }
    private void ApplyProductSearch()
    {
        if (!_ready) return;
        var term=ProductSearch.Text.Trim();
        var view=CollectionViewSource.GetDefaultView(ProductList.ItemsSource);
        view.Filter=o=>o is ProductChoice c && c.Product.Name.Contains(term,StringComparison.CurrentCultureIgnoreCase);
        view.Refresh();
    }
    private void Search_Changed(object sender,TextChangedEventArgs e) => ApplyProductSearch();
    private void Choice_Changed(object? sender,PropertyChangedEventArgs e)
    {
        if (!_loading) { UpdateSelectionText(); _filters.Update(); }
    }
    private void UpdateSelectionText() => SelectionText.Text=$"Отмечено: {_choices.Count(c=>c.IsSelected)} из {_choices.Count}. Поиск скрывает строки, но сохраняет отметки.";
    private void SetVisible(bool selected)
    {
        var term=ProductSearch.Text.Trim();
        _loading=true;
        try { foreach(var choice in _choices.Where(c=>c.Product.Name.Contains(term,StringComparison.CurrentCultureIgnoreCase))) choice.IsSelected=selected; }
        finally { _loading=false; }
        UpdateSelectionText(); _filters.Update();
    }
    private void SelectVisible_Click(object sender,RoutedEventArgs e) => SetVisible(true);
    private void ClearVisible_Click(object sender,RoutedEventArgs e) => SetVisible(false);
    private void UpdateResultState()
    {
        if (!_ready || _loading) return;
        if (_filters.IsDirty)
        {
            LastRows=null; AppliedSummary="";
            PeriodText.Text=_appliedRows is null ? "Параметры изменены. Нажмите «Применить»." : "Показан прежний результат. Параметры или данные изменились — нажмите «Применить».";
            ResultsInvalidated?.Invoke(this,EventArgs.Empty);
        }
        else
        {
            LastRows=_appliedRows; AppliedSummary=_appliedSummary;
            PeriodText.Text=_appliedRows is null ? "Откройте фильтры, отметьте товары и нажмите «Применить»." : _appliedSummary;
        }
    }
    private void Calculate_Click(object sender,RoutedEventArgs e) => Ui.Run(() =>
    {
        var products=_choices.Where(c=>c.IsSelected).Select(c=>c.Product).ToList();
        if (products.Count==0) throw new ArgumentException("Отметьте хотя бы один товар.");
        if (FromPicker.SelectedDate is not DateTime from || ToPicker.SelectedDate is not DateTime to)
            throw new ArgumentException("Укажите начальную и конечную даты.");
        var interval=Ui.BuildInterval(from,FromTimeBox.Text,to,ToTimeBox.Text);
        var start=interval.From!.Value; var end=interval.ToExclusive!.Value;
        var now=_context.Repository.GetServerTime();
        if (to.Date>now.Date) throw new ArgumentException("Конечная дата не может быть позже сегодняшней даты SQL Server.");
        if (end>now) end=now;
        if (end<=start) throw new ArgumentException("Для выбранного периода ещё нет прошедшего времени.");
        var warehouse=Ui.SelectedId(WarehouseBox);
        var movements=_context.Repository.GetMovements(products.Select(p=>p.Id),end);
        var rows=TurnoverCalculator.Calculate(products,movements,start,end,warehouse);
        _appliedRows=rows; LastRows=rows; GridView.ItemsSource=rows;
        _appliedSummary=$"Период: {start:dd.MM.yyyy HH:mm:ss} — {end:dd.MM.yyyy HH:mm:ss} (конец не включается); склад: {(WarehouseBox.SelectedItem as LookupItem)?.Name}; позиций: {rows.Count}.";
        AppliedSummary=_appliedSummary; PeriodText.Text=AppliedSummary; _filters.MarkApplied(); ResultsInvalidated?.Invoke(this,EventArgs.Empty);
    });
    private void Reset_Click(object sender,RoutedEventArgs e) => Ui.Run(() =>
    {
        var today=_context.Repository.GetServerTime().Date;
        _loading=true;
        try { FromPicker.SelectedDate=today.AddDays(-29); ToPicker.SelectedDate=today; WarehouseBox.SelectedIndex=0; FromTimeBox.Text=ToTimeBox.Text=ProductSearch.Text=""; foreach(var c in _choices) c.IsSelected=true; }
        finally { _loading=false; }
        ApplyProductSearch(); UpdateSelectionText(); _filters.Update(); UpdateResultState();
    });

    public sealed class ProductChoice(Product product,bool selected) : INotifyPropertyChanged
    {
        private bool _isSelected=selected;
        public Product Product { get; }=product;
        public bool IsSelected
        {
            get=>_isSelected;
            set { if (_isSelected==value) return; _isSelected=value; PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(IsSelected))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
