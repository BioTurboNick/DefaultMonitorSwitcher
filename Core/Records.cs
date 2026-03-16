namespace DefaultMonitorSwitcher.Core;

public sealed record MonitorInfo
{
    public required string DevicePath { get; init; }
    public required string FriendlyName { get; init; }
    public required System.Drawing.Rectangle Bounds { get; init; }
    public bool IsPrimary { get; init; }
    /// <summary>Position-qualified label, e.g. "Left — Samsung…". Set by DisplayService.</summary>
    public string DisplayLabel { get; init; } = "";
}

public sealed record AudioEndpointInfo
{
    public required string DeviceId { get; init; }
    public required string FriendlyName { get; init; }
}

public sealed record ActivitySample
{
    public required DateTimeOffset Timestamp            { get; init; }
    public required ActivityZone   CursorZone           { get; init; }
    public required ActivityZone   ForegroundWindowZone { get; init; }
    /// <summary>
    /// True if DXGI frame activity or WASAPI audio output was detected on the HDTV
    /// this tick (§4.4). Always false when HdtvEngagementDetectionEnabled is false.
    /// </summary>
    public required bool           IsHdtvEngaged        { get; init; }
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
    /// <summary>HDTV dwell threshold used when elevated polling is active (window-drag detected).</summary>
    public int    ElevatedHdtvDwellSeconds         { get; init; } = 12;
    public int    MouseDwellSeconds                { get; init; } = 10;
    public int    PollIntervalSeconds              { get; init; } = 5;
    public int    ElevatedPollIntervalSeconds      { get; init; } = 1;
    public int    ElevatedPollDurationSeconds      { get; init; } = 30;
    public bool   TvShowModeEnabled                { get; init; } = false;
    /// <summary>
    /// When true, idle detection uses GetLastInputInfo + DXGI frame activity + WASAPI
    /// audio peak rather than foreground window zone attribution (§5.1, §4.4).
    /// </summary>
    public bool   HdtvEngagementDetectionEnabled   { get; init; } = true;
    public bool   RunOnStartup                     { get; init; } = true;
}
