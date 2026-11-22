using System.Windows;
using kursa.Infrastructure;
using Warehouse.Core.Models;

namespace kursa.Views.Dialogs;
public partial class UserWindow : Window
{
    private readonly WorkspaceContext _context;
    private readonly int _id;
    public UserWindow(WorkspaceContext context,UserAccount? account)
    {
        InitializeComponent(); _context=context; _id=account?.Id ?? 0;
        Heading.Text=_id==0 ? "Новый пользователь" : "Изменить пользователя";
        PasswordLabel.Text=_id==0 ? "Пароль" : "Новый пароль (необязательно)";
        LoginBox.Text=account?.Login ?? ""; NameBox.Text=account?.FullName ?? "";
        RoleBox.ItemsSource=Enum.GetValues<UserRole>().Select(r=>new RoleChoice(r,UserRoles.Name(r))).ToList();
        RoleBox.DisplayMemberPath=nameof(RoleChoice.Name); RoleBox.SelectedValuePath=nameof(RoleChoice.Role);
        RoleBox.SelectedValue=account?.Role ?? UserRole.Manager;
    }
    private void Save_Click(object sender,RoutedEventArgs e) => Ui.Run(() =>
    {
        if (PasswordBox.Password!=ConfirmBox.Password) throw new ArgumentException("Пароли не совпадают.");
        if (RoleBox.SelectedValue is not UserRole role) throw new ArgumentException("Выберите роль.");
        _context.Repository.SaveUser(new UserAccount {Id=_id,Login=LoginBox.Text,FullName=NameBox.Text,Role=role},PasswordBox.Password,_context.User);
        _context.NotifyDataChanged(); DialogResult=true;
    });
    private sealed record RoleChoice(UserRole Role,string Name);
}
