using DefaultMonitorSwitcher.Core;

namespace DefaultMonitorSwitcher.Services;

public sealed class StartupService : IStartupService
{
    public bool IsRegistered => false;
    public void Register() { }
    public void Unregister() { }
}
