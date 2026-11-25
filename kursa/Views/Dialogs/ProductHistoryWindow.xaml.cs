using System.Globalization;
using System.Windows;
using kursa.Infrastructure;
using Warehouse.Core.Models;

namespace kursa.Views.Dialogs;

/// <summary>Все операции по конкретному товару: приходы, реализации и перемещения.</summary>
public partial class ProductHistoryWindow : Window
{
    private readonly WorkspaceContext _context;
    private readonly int _productId;
    public ProductHistoryWindow(WorkspaceContext context, int productId, string productName)
    {
        InitializeComponent(); _context=context; _productId=productId;
        Heading.Text=$"История товара «{productName}»"; Title=Heading.Text;
        Ui.SetLookup(KindBox,[new(null,"Все операции"),new((int)OperationKind.Receipt,"Приход"),new((int)OperationKind.Shipment,"Реализация"),new((int)OperationKind.Transfer,"Перемещение")]);
        Ui.SetLookup(WarehouseBox,new[] {new LookupItem(null,"Все склады")}.Concat(context.Repository.GetWarehouses().Select(w=>new LookupItem(w.Id,w.Name))));
        Apply();
    }
    private void Apply() => Ui.Run(() =>
    {
        var interval=Ui.BuildInterval(Ui.ReadDate(FromPicker,"С даты"),null,Ui.ReadDate(ToPicker,"По дату"),null);
        var kind=Ui.SelectedId(KindBox);
        var rows=_context.Repository.GetOperations(new OperationFilter { ProductId=_productId, FromInclusive=interval.From, ToExclusive=interval.ToExclusive,
            WarehouseId=Ui.SelectedId(WarehouseBox), Kind=kind.HasValue ? (OperationKind)kind.Value : null });
        GridView.ItemsSource=rows;
        var culture=CultureInfo.CurrentCulture; var unit=rows.FirstOrDefault()?.Unit ?? "";
        decimal Sum(OperationKind k) => rows.Where(x=>x.Kind==k).Sum(x=>x.Quantity);
        var stock=_context.Repository.GetStocks(new StockFilter { ProductId=_productId }).Sum(x=>x.Quantity);
        SummaryText.Text=$"Операций: {rows.Count} · поступило {Sum(OperationKind.Receipt).ToString("N3",culture)} · реализовано {Sum(OperationKind.Shipment).ToString("N3",culture)}"
            + $" · перемещено {Sum(OperationKind.Transfer).ToString("N3",culture)} {unit} · текущий остаток на всех складах: {stock.ToString("N3",culture)}";
        SubHeading.Text="Позиции документов по этому товару; время — SQL Server. Фильтр по датам включает оба дня.";
    });
    private void Apply_Click(object sender, RoutedEventArgs e) => Apply();
    private void Reset_Click(object sender, RoutedEventArgs e) { FromPicker.SelectedDate=null; ToPicker.SelectedDate=null; KindBox.SelectedIndex=WarehouseBox.SelectedIndex=0; Apply(); }
    private void Open_Click(object sender, RoutedEventArgs e) => Ui.Run(() =>
    {
        if (GridView.SelectedItem is not OperationRow row) { if (sender==OpenButton) Ui.Error("Выберите операцию."); return; }
        new DocumentDetailsWindow(_context,row.DocumentId) { Owner=this }.Show();
    });
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
