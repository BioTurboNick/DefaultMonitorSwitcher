namespace DefaultMonitorSwitcher.Core;

public interface IDisplayService
{
    IReadOnlyList<MonitorInfo> GetActiveMonitors();
    MonitorInfo? MonitorFromPoint(System.Drawing.Point point);
    MonitorInfo? MonitorFromWindowHandle(nint hwnd);
    string? GetPrimaryMonitorDevicePath();

    /// <summary>
    /// Sets the specified monitor as the Windows primary display.
    /// Adjusts all other monitors' positions so the new primary is at (0,0).
    /// Returns false on failure; populates failureReason.
    /// </summary>
    bool TrySetPrimaryMonitor(string devicePath, out string? failureReason);
}
