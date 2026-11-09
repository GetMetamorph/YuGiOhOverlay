using System.Windows;

namespace YuGiOhOverlay.UI;

public partial class App : System.Windows.Application
{
    private CompositionRoot? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _services = new CompositionRoot();
        MainWindow = new MainWindow(_services);
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
