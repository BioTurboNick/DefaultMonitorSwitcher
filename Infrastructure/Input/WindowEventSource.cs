using DefaultMonitorSwitcher.Core;

namespace DefaultMonitorSwitcher.Infrastructure.Input;

public sealed class WindowEventSource : IWindowEventSource
{
    public event EventHandler? WindowMovedToHdtv;
    public void Start(MonitorInfo hdtvMonitor) { }
    public void Stop() { }
    public void Dispose() { }
}
