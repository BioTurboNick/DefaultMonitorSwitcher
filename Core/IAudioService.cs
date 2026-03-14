namespace DefaultMonitorSwitcher.Core;

public interface IAudioService
{
    IReadOnlyList<AudioEndpointInfo> GetPlaybackEndpoints();
    string? GetDefaultPlaybackDeviceId();

    /// <summary>
    /// Case-insensitive substring match of monitor.FriendlyName in endpoint.FriendlyName.
    /// Returns the best match or null.
    /// </summary>
    AudioEndpointInfo? AutoDetectEndpointForMonitor(MonitorInfo monitor);

    /// <summary>
    /// Sets the Windows default playback device for all three ERole values via IAudioPolicyConfig.
    /// Returns false (does not throw) on COM failure; populates failureReason.
    /// </summary>
    bool TrySetDefaultPlaybackDevice(string deviceId, out string? failureReason);
}
