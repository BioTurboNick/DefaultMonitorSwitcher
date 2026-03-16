namespace DefaultMonitorSwitcher.Core;

public interface IHdtvEngagementDetector : IDisposable
{
    /// <summary>
    /// Samples HDTV engagement for the current poll tick. Returns true if DXGI frame
    /// activity or a non-zero WASAPI audio peak is detected on the HDTV endpoint.
    /// Always returns false when HdtvEngagementDetectionEnabled is false.
    /// Thread-safe; called from the background poll thread.
    /// </summary>
    bool IsEngaged();

    /// <summary>
    /// Updates the HDTV target. Must be called when the HDTV monitor identity or
    /// audio device ID changes (config save, display topology change).
    /// Thread-safe.
    /// </summary>
    void Configure(MonitorInfo? hdtvMonitor, string? hdtvAudioDeviceId);
}
