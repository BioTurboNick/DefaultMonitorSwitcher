namespace DefaultMonitorSwitcher.Core;

public sealed record MonitorInfo
{
    public required string DevicePath { get; init; }
    public required string FriendlyName { get; init; }
    public required System.Drawing.Rectangle Bounds { get; init; }
    public bool IsPrimary { get; init; }
}

public sealed record AudioEndpointInfo
{
    public required string DeviceId { get; init; }
    public required string FriendlyName { get; init; }
}

public sealed record ActivitySample
{
    public required DateTimeOffset Timestamp { get; init; }
    public required ActivityZone CursorZone { get; init; }
    public required ActivityZone ForegroundWindowZone { get; init; }
    public ActivityZone EffectiveZone =>
        ForegroundWindowZone != ActivityZone.None ? ForegroundWindowZone : CursorZone;
}

public sealed record AppConfiguration
{
    public string? HdtvDisplayDevicePath { get; init; }
    public string? PreferredPrimaryDisplayDevicePath { get; init; }
    public string? HdtvAudioDeviceId { get; init; }
    /// <summary>null = auto-detected from preferred primary monitor's EDID name.</summary>
    public string? DesktopAudioDeviceId { get; init; }
    public bool   AudioSwitchingEnabled            { get; init; } = true;
    public bool   RespectManualAudioOverride       { get; init; } = false;
    public int    IdleTimeoutSeconds               { get; init; } = 300;
    public int    DesktopDwellSeconds              { get; init; } = 120;
    public int    HdtvDwellSeconds                 { get; init; } = 60;
    public int    MouseDwellSeconds                { get; init; } = 10;
    public int    PollIntervalSeconds              { get; init; } = 5;
    public int    ElevatedPollIntervalSeconds      { get; init; } = 1;
    public int    ElevatedPollDurationSeconds      { get; init; } = 30;
    public bool   TvShowModeEnabled                { get; init; } = false;
    public bool   RunOnStartup                     { get; init; } = true;
}
