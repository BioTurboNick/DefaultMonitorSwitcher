using System.Windows;

namespace DefaultMonitorSwitcher;

public partial class App : Application
{
    private AppBootstrapper? _bootstrapper;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _bootstrapper = new AppBootstrapper();
        _bootstrapper.Start();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        _bootstrapper?.OnSessionEnding();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _bootstrapper?.Dispose();
        base.OnExit(e);
    }
}
