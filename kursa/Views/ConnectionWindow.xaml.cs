using System.Windows;
using System.Windows.Input;
using kursa.Infrastructure;
using Warehouse.Core.Data;

namespace kursa.Views;

public partial class ConnectionWindow : Window
{
    public ConnectionSettings Settings { get; private set; }
    public ConnectionWindow(ConnectionSettings settings)
    {
        InitializeComponent(); Settings = settings;
        ServerBox.Text = settings.Server; DatabaseBox.Text = settings.Database;
        WindowsAuth.IsChecked = settings.IntegratedSecurity; SqlAuth.IsChecked = !settings.IntegratedSecurity;
        LoginBox.Text = settings.SqlLogin; PasswordBox.Password = settings.SqlPassword;
        TrustCertificateBox.IsChecked = settings.TrustServerCertificate;
    }
    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        Ui.Run(() =>
        {
            var settings = new ConnectionSettings { Server=ServerBox.Text, Database=DatabaseBox.Text,
                IntegratedSecurity=WindowsAuth.IsChecked == true, SqlLogin=LoginBox.Text, SqlPassword=PasswordBox.Password,
                TrustServerCertificate=TrustCertificateBox.IsChecked == true };
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                DatabaseInitializer.Initialize(settings.BuildConnectionString());
                SettingsStore.Save(settings);
                Settings = settings;
            }
            finally { Mouse.OverrideCursor = null; }
            DialogResult = true;
        });
    }
}
