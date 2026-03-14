using DefaultMonitorSwitcher.Core;

namespace DefaultMonitorSwitcher.Infrastructure.Display;

public sealed class DisplayService : IDisplayService
{
    public IReadOnlyList<MonitorInfo> GetActiveMonitors() => [];
    public MonitorInfo? MonitorFromPoint(System.Drawing.Point point) => null;
    public MonitorInfo? MonitorFromWindowHandle(nint hwnd) => null;
    public string? GetPrimaryMonitorDevicePath() => null;
    public bool TrySetPrimaryMonitor(string devicePath, out string? failureReason)
    {
        failureReason = "Not implemented";
        return false;
    }
}
