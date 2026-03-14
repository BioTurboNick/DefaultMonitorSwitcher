namespace DefaultMonitorSwitcher.Core;

public interface IWindowEventSource : IDisposable
{
    /// <summary>
    /// Fired on the UI thread when a top-level window finishes moving and resolves
    /// to the HDTV monitor. Not fired for moves to any other monitor.
    /// </summary>
    event EventHandler? WindowMovedToHdtv;

    /// <summary>Must be called on the UI thread (WinEventHook requires a message loop).</summary>
    void Start(MonitorInfo hdtvMonitor);
    void Stop();
}
