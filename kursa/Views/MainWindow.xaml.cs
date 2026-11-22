using System.Windows;
using System.Windows.Controls;
using kursa.Infrastructure;
using kursa.Views.Dialogs;

namespace kursa.Views;
public partial class MainWindow : Window
{
    private readonly WorkspaceContext _context;
    private readonly ProductsView _products;
    private readonly WarehousesView _warehouses;
    private readonly CounterpartiesView _counterparties;
    private readonly OperationsView _operations;
    private readonly StocksView _stocks;
    private readonly TurnoverView _turnover;
    private bool _ready;
    public bool LogoutRequested { get; private set; }
    public MainWindow(WorkspaceContext context)
    {
        InitializeComponent(); _context=context;
        UserText.Text=context.User.FullName; RoleText.Text=context.User.RoleName;
        _products=new ProductsView(context); _warehouses=new WarehousesView(context); _counterparties=new CounterpartiesView(context);
        _operations=new OperationsView(context); _stocks=new StocksView(context); _turnover=new TurnoverView(context);
        ProductsHost.Content=_products; WarehousesHost.Content=_warehouses; CounterpartiesHost.Content=_counterparties;
        OperationsHost.Content=_operations; StocksHost.Content=_stocks; TurnoverHost.Content=_turnover;
        ReportsHost.Content=new ReportsView(context,_stocks,_turnover);
        ProductsTab.IsEnabled=WarehousesTab.IsEnabled=CounterpartiesTab.IsEnabled=context.User.CanManageReferences;
        TurnoverTab.IsEnabled=ReportsTab.IsEnabled=context.User.CanAnalyze;
        _ready=true;
        if (!context.User.CanManageReferences) Tabs.SelectedItem=OperationsTab;
        RefreshSelected();
    }
    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ready && ReferenceEquals(e.OriginalSource,Tabs)) RefreshSelected();
    }
    private void RefreshSelected()
    {
        if (Tabs.SelectedItem==ProductsTab) _products.Refresh();
        else if (Tabs.SelectedItem==WarehousesTab) _warehouses.Refresh();
        else if (Tabs.SelectedItem==CounterpartiesTab) _counterparties.Refresh();
        else if (Tabs.SelectedItem==OperationsTab) _operations.Refresh();
        else if (Tabs.SelectedItem==StocksTab) _stocks.Refresh();
        else if (Tabs.SelectedItem==TurnoverTab) _turnover.Refresh();
    }
    private void Password_Click(object sender, RoutedEventArgs e) => new PasswordWindow(_context) {Owner=this}.ShowDialog();
    private void Logout_Click(object sender, RoutedEventArgs e) { LogoutRequested=true; Close(); }
}
