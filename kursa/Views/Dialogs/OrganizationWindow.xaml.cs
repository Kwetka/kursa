using System.Windows;
using kursa.Infrastructure;
using Warehouse.Core.Models;

namespace kursa.Views.Dialogs;
public partial class OrganizationWindow : Window
{
    private readonly WorkspaceContext _context;
    public OrganizationWindow(WorkspaceContext context)
    {
        InitializeComponent(); _context=context;
        var profile=context.Repository.GetOrganization();
        NameBox.Text=profile.Name; UnpBox.Text=profile.Unp; AddressBox.Text=profile.Address; PhoneBox.Text=profile.Phone;
        ReleaseBox.Text=profile.ReleaseAllowedBy; VatBox.Text=Ui.FormatNumber(profile.DefaultVatRate);
        if (!context.User.CanManageOrganization)
        {
            foreach (var box in new[] {NameBox,UnpBox,AddressBox,PhoneBox,ReleaseBox}) box.IsReadOnly=true;
            VatBox.IsEnabled=false; SaveButton.Visibility=Visibility.Collapsed; ReadOnlyHint.Visibility=Visibility.Visible;
        }
    }
    private void Save_Click(object sender, RoutedEventArgs e) => Ui.Run(() =>
    {
        _context.Repository.SaveOrganization(new OrganizationProfile { Name=NameBox.Text, Unp=UnpBox.Text, Address=AddressBox.Text,
            Phone=PhoneBox.Text, ReleaseAllowedBy=ReleaseBox.Text, DefaultVatRate=Ui.ParseNumber(VatBox.Text,"Ставка НДС по умолчанию") },_context.User);
        _context.NotifyDataChanged(); DialogResult=true;
    });
}
