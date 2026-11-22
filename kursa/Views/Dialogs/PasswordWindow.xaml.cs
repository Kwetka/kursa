using System.Windows;
using kursa.Infrastructure;

namespace kursa.Views.Dialogs;
public partial class PasswordWindow : Window
{
    private readonly WorkspaceContext _context;
    public PasswordWindow(WorkspaceContext context) { InitializeComponent(); _context=context; }
    private void Save_Click(object sender, RoutedEventArgs e) => Ui.Run(() =>
    {
        if (NewBox.Password != ConfirmBox.Password) throw new ArgumentException("Новые пароли не совпадают.");
        _context.Repository.ChangePassword(_context.User,CurrentBox.Password,NewBox.Password);
        MessageBox.Show("Пароль изменён.","Склад",MessageBoxButton.OK,MessageBoxImage.Information); DialogResult=true;
    });
}
