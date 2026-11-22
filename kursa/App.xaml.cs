using System.Windows;
using System.Globalization;
using kursa.Infrastructure;
using kursa.Views;
using Warehouse.Core.Data;

namespace kursa;

public partial class App : Application
{
    private ConnectionSettings? _settings;
    private WarehouseRepository? _repository;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // A Russian/Belarusian locale keeps dates and decimal amounts predictable.
        var culture = CultureInfo.GetCultureInfo("ru-BY");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        try { _settings = SettingsStore.Load(); }
        catch (Exception ex) { Ui.Error("Не удалось прочитать настройки подключения. Настройте подключение заново.\n\n" + ex.Message); }
        if (_settings is not null)
        {
            try
            {
                var cs = _settings.BuildConnectionString();
                DatabaseInitializer.Initialize(cs);
                _repository = new WarehouseRepository(cs);
            }
            catch (Exception ex)
            {
                Ui.Error("Не удалось подключиться к SQL Server. Проверьте настройки.\n\n" + ex.Message);
                _repository = null;
            }
        }
        if (_repository is null && !ConfigureConnection()) { Shutdown(); return; }
        ShowLogin();
    }

    private bool ConfigureConnection()
    {
        var dialog = new ConnectionWindow(_settings ?? new ConnectionSettings());
        if (dialog.ShowDialog() != true) return false;
        _settings = dialog.Settings;
        _repository = new WarehouseRepository(_settings.BuildConnectionString());
        return true;
    }

    private void ShowLogin()
    {
        while (true)
        {
            var login = new LoginWindow(_repository!);
            var result = login.ShowDialog();
            if (login.ChangeConnectionRequested) { ConfigureConnection(); continue; }
            if (result != true || login.User is null) { Shutdown(); return; }
            var window = new MainWindow(new WorkspaceContext(_repository!, login.User));
            MainWindow = window;
            window.Closed += (_, _) => { if (window.LogoutRequested) ShowLogin(); else Shutdown(); };
            window.Show();
            return;
        }
    }
}
