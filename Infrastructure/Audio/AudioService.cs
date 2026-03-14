using DefaultMonitorSwitcher.Core;

namespace DefaultMonitorSwitcher.Infrastructure.Audio;

public sealed class AudioService : IAudioService
{
    public IReadOnlyList<AudioEndpointInfo> GetPlaybackEndpoints() => [];
    public string? GetDefaultPlaybackDeviceId() => null;
    public AudioEndpointInfo? AutoDetectEndpointForMonitor(MonitorInfo monitor) => null;
    public bool TrySetDefaultPlaybackDevice(string deviceId, out string? failureReason)
    {
        failureReason = "Not implemented";
        return false;
    }
}
