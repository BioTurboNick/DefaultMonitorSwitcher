namespace DefaultMonitorSwitcher.Core;

public interface IStartupService
{
    bool IsRegistered { get; }
    void Register();
    void Unregister();
}
