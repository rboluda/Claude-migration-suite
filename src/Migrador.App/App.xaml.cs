using System.Windows;
using System.Windows.Threading;
using Migrador.App.Services;
using Migrador.App.ViewModels;
using Migrador.App.Views;
using Migrador.Core.Localization;
using static Migrador.Core.Localization.Loc;

namespace Migrador.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var settings = new SettingsService();
        Loc.Activate(settings.Current.Language);
        UpdateService.Cleanup();

        var main = new MainViewModel(settings, new UpdateService(settings));
        var window = new MainWindow { DataContext = main };
        MainWindow = window;
        window.Show();
        _ = main.StartAsync(e.Args);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Unattended update: a version downloaded and verified while the app was open replaces it now.
        UpdateService.ApplyPending();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(MainWindow!, e.Exception.Message, T("Algo ha fallado"), MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
