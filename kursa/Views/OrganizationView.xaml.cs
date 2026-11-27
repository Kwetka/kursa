using System.Windows;
using System.Windows.Controls;
using kursa.Infrastructure;
using Warehouse.Core.Models;

namespace kursa.Views;
public partial class OrganizationView : UserControl
{
    private readonly WorkspaceContext _context;
    public OrganizationView(WorkspaceContext context)
    {
        InitializeComponent(); _context=context;
        var canEdit=context.User.CanManageWarehousesAndParties;
        NameBox.IsReadOnly=AddressBox.IsReadOnly=UnpBox.IsReadOnly=!canEdit;
        SaveButton.IsEnabled=canEdit;
    }
    public void Refresh() => Ui.Run(() =>
    {
        var data=_context.Repository.GetOrganization(); NameBox.Text=data.Name; AddressBox.Text=data.Address; UnpBox.Text=data.Unp;
    });
    private void Save_Click(object sender,RoutedEventArgs e) => Ui.Run(() =>
    {
        _context.Repository.SaveOrganization(new PartyDetails {Name=NameBox.Text,Address=AddressBox.Text,Unp=UnpBox.Text},_context.User);
        _context.NotifyDataChanged(); MessageBox.Show("Реквизиты сохранены.","Организация",MessageBoxButton.OK,MessageBoxImage.Information);
    });
}
