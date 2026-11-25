// Windows-only, STA WPF state test. No database connection, no windows shown, no user data modified.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using kursa.Infrastructure;
using kursa.Views;
using Warehouse.Core.Data;
using Warehouse.Core.Models;

internal static class Program
{
    private static int _checks;
    [STAThread]
    private static int Main()
    {
        try
        {
            var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
            app.Resources.MergedDictionaries.Add(new ResourceDictionary {Source=new Uri("pack://application:,,,/kursa;component/Themes/Styles.xaml")});
            Run();
            Console.WriteLine($"WPF state checks passed: {_checks}.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static T Named<T>(FrameworkElement root,string name) where T:class
        => root.FindName(name) as T ?? throw new InvalidOperationException($"{root.GetType().Name}: missing {name}.");
    private static void Check(bool value,string description)
    {
        if (!value) throw new Exception("FAILED: "+description);
        _checks++; Console.WriteLine("OK: "+description);
    }
    private static Viewbox Icon(Button button) => ((StackPanel)button.Content).Children.OfType<Viewbox>().Single();
    private static bool IsDirty(Button button) => Icon(button).Visibility==Visibility.Visible;

    private static void Run()
    {
        // Constructor parses the string only; no SQL connection is opened by these tests.
        var repo=new WarehouseRepository("Server=unused;Database=unused;Integrated Security=true;TrustServerCertificate=true");
        var admin=new AppUser(1,"admin","Admin",UserRole.Admin);
        var context=new WorkspaceContext(repo,admin);
        FrameworkElement[] views=[new ProductsView(context),new WarehousesView(context),new CounterpartiesView(context),
            new OperationsView(context),new StocksView(context),new TurnoverView(context),new UsersView(context),new AuditView(context)];
        foreach(var view in views)
        {
            var expander=Named<Expander>(view,"FiltersExpander");
            Check(!expander.IsExpanded,view.GetType().Name+": filters initially hidden");
            expander.IsExpanded=true; Check(expander.IsExpanded,view.GetType().Name+": filters open");
            expander.IsExpanded=false; Check(!expander.IsExpanded,view.GetType().Name+": filters close");
            var input=Named<TextBox>(view,view is TurnoverView ? "FromTimeBox" : "SearchBox");
            var apply=Named<Button>(view,"ApplyButton");
            Check(!IsDirty(apply),view.GetType().Name+": indicator initially hidden");
            input.Text="changed";
            Check(IsDirty(apply) && expander.Header.ToString()!.Contains("неприменённые"),view.GetType().Name+": edits display pending icon and header");
            input.Text="";
            Check(!IsDirty(apply),view.GetType().Name+": restoring applied values clears indicator");
        }
        var button=new Button(); var panel=new Expander(); var field=new TextBox();
        var indicator=new FilterIndicator(button,panel,()=>[field.Text]); indicator.Track(field);
        var vector=(Canvas)Icon(button).Child;
        Check(vector.Children.OfType<Path>().Any(),"refresh icon is a vector, not an external image");
        field.Text="new"; Check(IsDirty(button),"changed value displays icon");
        indicator.MarkApplied(); Check(!IsDirty(button),"successful apply removes icon");
        field.Text="old"; Check(IsDirty(button),"another edit displays icon again");
        field.Text="new"; Check(!IsDirty(button),"reverting to last applied value removes icon");
        indicator.ForceDirty(); Check(IsDirty(button),"changed underlying data can force stale state");
        indicator.MarkApplied(); Check(!IsDirty(button),"successful refresh clears forced stale state");

        foreach(var (role,products,receipt,shipment,transfer) in new[] {
            (UserRole.Admin,true,true,true,true),(UserRole.Manager,false,false,false,false),
            (UserRole.Storekeeper,true,true,false,true),(UserRole.SalesManager,false,false,true,false) })
        {
            var user=new AppUser(1,"test","Test",role); var c=new WorkspaceContext(repo,user);
            var p=new ProductsView(c); var o=new OperationsView(c); var w=new WarehousesView(c); var cp=new CounterpartiesView(c);
            Check(Named<StackPanel>(p,"ActionsPanel").IsEnabled==products,$"{role}: product controls");
            Check(Named<Button>(o,"ReceiptButton").IsEnabled==receipt,$"{role}: receipt control");
            Check(Named<Button>(o,"ShipmentButton").IsEnabled==shipment,$"{role}: shipment control");
            Check(Named<Button>(o,"TransferButton").IsEnabled==transfer,$"{role}: transfer control");
            Check(Named<StackPanel>(w,"ActionsPanel").IsEnabled==(role==UserRole.Admin),$"{role}: warehouse controls (initial setup — administrator only)");
            Check(Named<StackPanel>(cp,"ActionsPanel").IsEnabled==(role!=UserRole.Manager),$"{role}: counterparty controls (suppliers — storekeeper, buyers — sales manager)");
            Check(Named<Button>(o,"OpenButton").IsEnabled && Named<Button>(o,"PrintButton").IsEnabled && Named<Button>(p,"HistoryButton").IsEnabled,$"{role}: document lines, TTN print and product history are available to every role");
        }
    }
}
