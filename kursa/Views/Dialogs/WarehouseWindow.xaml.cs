using System.Windows;
using kursa.Infrastructure;
using WarehouseEntity = Warehouse.Core.Models.Warehouse;

namespace kursa.Views.Dialogs;
public partial class WarehouseWindow : Window
{
    private readonly WorkspaceContext _context;
    private readonly int _id;
    public WarehouseWindow(WorkspaceContext context, WarehouseEntity? warehouse)
    {
        InitializeComponent(); _context=context; _id=warehouse?.Id ?? 0;
        Heading.Text=_id==0 ? "Новый склад" : "Изменить склад";
        NameBox.Text=warehouse?.Name ?? ""; AddressBox.Text=warehouse?.Address ?? "";
        ManagerBox.Text=warehouse?.ManagerName ?? ""; ContactBox.Text=warehouse?.ManagerContact ?? "";
    }
    private void Save_Click(object sender, RoutedEventArgs e) => Ui.Run(() =>
    {
        _context.Repository.SaveWarehouse(new WarehouseEntity { Id=_id, Name=NameBox.Text, Address=AddressBox.Text,
            ManagerName=ManagerBox.Text, ManagerContact=ContactBox.Text },_context.User);
        _context.NotifyDataChanged(); DialogResult=true;
    });
}
