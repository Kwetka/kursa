using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using kursa.Infrastructure;
using kursa.Views.Dialogs;
using Warehouse.Core.Models;

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
    private readonly UsersView _users;
    private readonly AuditView _audit;
    private readonly DispatcherTimer _auditTimer;
    private bool _ready, _checkingAudit;
    public bool LogoutRequested { get; private set; }
    public MainWindow(WorkspaceContext context)
    {
        InitializeComponent(); _context=context;
        UserText.Text=context.User.FullName; RoleText.Text=context.User.RoleName;
        _products=new ProductsView(context); _warehouses=new WarehousesView(context); _counterparties=new CounterpartiesView(context);
        _operations=new OperationsView(context); _stocks=new StocksView(context); _turnover=new TurnoverView(context);
        _users=new UsersView(context); _audit=new AuditView(context);
        ProductsHost.Content=_products; WarehousesHost.Content=_warehouses; CounterpartiesHost.Content=_counterparties;
        OperationsHost.Content=_operations; StocksHost.Content=_stocks; TurnoverHost.Content=_turnover;
        ReportsHost.Content=new ReportsView(context,_stocks,_turnover); UsersHost.Content=_users; AuditHost.Content=_audit;
        TurnoverTab.IsEnabled=ReportsTab.IsEnabled=context.User.CanAnalyze; UsersTab.IsEnabled=context.User.CanManageUsers;
        _ready=true;
        if (!context.User.CanManageProducts) Tabs.SelectedItem=context.User.CanShip ? OperationsTab : StocksTab;
        context.Repository.AuditStatusChanged+=Repository_AuditStatusChanged;
        context.DataChanged+=Context_DataChanged;
        UpdateAuditStatus(context.Repository.AuditStatus); UpdateLegacyWarning();
        _auditTimer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(30)};
        _auditTimer.Tick+=AuditTimer_Tick; _auditTimer.Start();
        Closed+=(_,_) => { _auditTimer.Stop(); context.Repository.AuditStatusChanged-=Repository_AuditStatusChanged; context.DataChanged-=Context_DataChanged; };
        RefreshSelected();
    }
    private void Tabs_SelectionChanged(object sender,SelectionChangedEventArgs e)
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
        else if (Tabs.SelectedItem==UsersTab && _context.User.CanManageUsers) _users.Refresh();
        else if (Tabs.SelectedItem==AuditTab) _audit.Refresh();
    }
    private void Repository_AuditStatusChanged(object? sender,AuditFileStatus status)
    {
        if (Dispatcher.CheckAccess()) UpdateAuditStatus(status);
        else Dispatcher.BeginInvoke(() => UpdateAuditStatus(status));
    }
    private void UpdateAuditStatus(AuditFileStatus status)
    {
        _audit.ShowStatus(status);
        bool warning=status.State is AuditFileState.Diverged or AuditFileState.Unavailable;
        AuditWarning.Visibility=warning ? Visibility.Visible : Visibility.Collapsed;
        AuditWarningText.Text=status.Message;
        AuditFooter.Text=status.State switch
        {
            AuditFileState.Verified => "SQL Server подключён · файл журнала сверён с SQL",
            AuditFileState.Diverged => "Журнал: обнаружено расхождение — подробности на вкладке «Журнал действий»",
            AuditFileState.Unavailable => "Журнал: файл недоступен — подробности на вкладке «Журнал действий»",
            _ => "SQL Server подключён · журнал ещё не проверен"
        };
        AuditFooter.Foreground=warning ? Brushes.DarkRed : Brushes.SlateGray;
    }
    private async void AuditTimer_Tick(object? sender,EventArgs e)
    {
        if (_checkingAudit) return;
        _checkingAudit=true;
        try { await Task.Run(() => _context.Repository.SyncAuditFile()); }
        finally { _checkingAudit=false; }
    }
    private void Context_DataChanged(object? sender,EventArgs e) => UpdateLegacyWarning();
    private void UpdateLegacyWarning()
    {
        try
        {
            var issues=_context.Repository.GetLegacyDataIssues();
            LegacyWarning.Visibility=issues.HasIssues ? Visibility.Visible : Visibility.Collapsed;
            LegacyWarningText.Text=$"Старые данные сохранены, но требуют исправления: товаров с нулевой ценой — {issues.Products}, складов с незаполненными полями — {issues.Warehouses}. Измените их в соответствующих справочниках.";
        }
        catch { /* Подробную ошибку покажет следующий запрос к SQL. */ }
    }
    private void Audit_Click(object sender,RoutedEventArgs e) => Tabs.SelectedItem=AuditTab;
    private void Password_Click(object sender,RoutedEventArgs e) => new PasswordWindow(_context) {Owner=this}.ShowDialog();
    private void Logout_Click(object sender,RoutedEventArgs e) { LogoutRequested=true; Close(); }
}
