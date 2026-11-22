using System.Windows;
using kursa.Infrastructure;
using Warehouse.Core.Data;
using Warehouse.Core.Models;

namespace kursa.Views;

public partial class LoginWindow : Window
{
    private readonly WarehouseRepository _repository;
    public AppUser? User { get; private set; }
    public bool ChangeConnectionRequested { get; private set; }
    public LoginWindow(WarehouseRepository repository) { InitializeComponent(); _repository = repository; }
    private void Login_Click(object sender, RoutedEventArgs e)
    {
        Ui.Run(() =>
        {
            User = _repository.Authenticate(LoginBox.Text, PasswordBox.Password);
            if (User is null) { Ui.Error("Неверный логин или пароль приложения."); return; }
            DialogResult = true;
        });
    }
    private void Connection_Click(object sender, RoutedEventArgs e) { ChangeConnectionRequested = true; DialogResult = false; }
}
