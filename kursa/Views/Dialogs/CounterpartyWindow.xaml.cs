using System.Windows;
using kursa.Infrastructure;
using Warehouse.Core.Models;

namespace kursa.Views.Dialogs;
public partial class CounterpartyWindow : Window
{
    private readonly WorkspaceContext _context;
    private readonly int _id;
    public CounterpartyWindow(WorkspaceContext context, Counterparty? party)
    {
        InitializeComponent(); _context=context; _id=party?.Id ?? 0;
        Heading.Text=_id==0 ? "Новый контрагент" : "Изменить контрагента";
        var user=context.User;
        // Поставщиков ведёт кладовщик (приход), покупателей — менеджер продаж (реализация).
        SupplierItem.IsEnabled=user.CanManageCounterparty(true); BuyerItem.IsEnabled=user.CanManageCounterparty(false);
        var supplier=party?.IsSupplier ?? user.CanManageCounterparty(true);
        TypeBox.SelectedIndex=supplier ? 0 : 1;
        TypeHint.Text=user.Role==UserRole.Admin ? "Поставщиков ведёт кладовщик, покупателей — менеджер продаж; администратор — всех."
            : supplier ? "Ваша роль ведёт поставщиков." : "Ваша роль ведёт покупателей.";
        NameBox.Text=party?.Name ?? ""; UnpBox.Text=party?.Unp ?? ""; AddressBox.Text=party?.Address ?? "";
        DeliveryBox.Text=party?.DeliveryAddress ?? ""; PhoneBox.Text=party?.Phone ?? ""; ContractBox.Text=party?.Contract ?? "";
    }
    private void Save_Click(object sender, RoutedEventArgs e) => Ui.Run(() =>
    {
        _context.Repository.SaveCounterparty(new Counterparty { Id=_id, Name=NameBox.Text, IsSupplier=TypeBox.SelectedIndex==0,
            Unp=UnpBox.Text, Address=AddressBox.Text, DeliveryAddress=DeliveryBox.Text, Phone=PhoneBox.Text, Contract=ContractBox.Text },_context.User);
        _context.NotifyDataChanged(); DialogResult=true;
    });
}
