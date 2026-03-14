using DefaultMonitorSwitcher.Core;

namespace DefaultMonitorSwitcher.Services;

/// <summary>
/// State machine that drives monitor and audio switching based on activity samples
/// and window-move events.
///
/// Thread safety: <see cref="IActivityTracker.SampleProduced"/> fires on a background
/// thread; <see cref="IWindowEventSource.WindowMovedToHdtv"/> fires on the UI thread.
/// All state reads/writes are protected by <see cref="_lock"/>. COM calls (display/audio
/// switches) are made outside the lock to avoid holding it during slow I/O.
/// </summary>
public sealed class SwitchController : ISwitchController
{
    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly IDisplayService        _display;
    private readonly IAudioService          _audio;
    private readonly IActivityTracker       _activity;
    private readonly IWindowEventSource     _windowEvents;
    private readonly IConfigurationService  _config;
    private readonly INotificationService   _notifications;

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly object   _lock            = new();
    private SwitcherState     _state           = SwitcherState.DesktopIdle;
    private bool              _tvShowMode;

    // Multi-purpose timestamp — semantics vary by state (see state machine):
    //   DesktopHdtvDwelling  → time we first saw HDTV activity
    //   HdtvActive           → time of last HDTV/any activity (for idle detection)
    //   HdtvIdleCountdown    → time we entered the countdown
    //   HdtvDesktopDwelling  → time we first saw desktop activity
    private DateTimeOffset _dwellStart = DateTimeOffset.UtcNow;

    // Last audio device ID we set (for RespectManualAudioOverride logic)
    private string? _lastSetAudioId;

    // ── ISwitchController ─────────────────────────────────────────────────────

    public SwitcherState CurrentState    { get { lock (_lock) return _state; } }
    public bool TvShowModeEnabled
    {
        get { lock (_lock) return _tvShowMode; }
        set { lock (_lock) _tvShowMode = value; }
    }

    public event EventHandler<SwitcherState>? StateChanged;
    public event EventHandler<(SwitchDirection Direction, SwitchReason Reason, SwitchResult Result)>? SwitchCompleted;

    // ── Constructor ───────────────────────────────────────────────────────────

    public SwitchController(
        IDisplayService        display,
        IAudioService          audio,
        IActivityTracker       activity,
        IWindowEventSource     windowEvents,
        IConfigurationService  config,
        INotificationService   notifications)
    {
        _display       = display;
        _audio         = audio;
        _activity      = activity;
        _windowEvents  = windowEvents;
        _config        = config;
        _notifications = notifications;
    }

    // ── Startup ───────────────────────────────────────────────────────────────

    public ValueTask StartAsync(CancellationToken ct = default)
    {
        var cfg = _config.Current;

        // Wire events before starting the tracker
        _activity.SampleProduced        += OnSampleProduced;
        _windowEvents.WindowMovedToHdtv += OnWindowMovedToHdtv;
        _config.ConfigurationChanged    += OnConfigurationChanged;

        // Start window hook if HDTV monitor is configured
        if (cfg.HdtvDisplayDevicePath != null)
        {
            var hdtv = _display.GetActiveMonitors()
                .FirstOrDefault(m => m.DevicePath.Equals(
                    cfg.HdtvDisplayDevicePath, StringComparison.OrdinalIgnoreCase));
            if (hdtv != null)
                _windowEvents.Start(hdtv);
        }

        _activity.Start();

        // If HDTV is already primary at startup, revert to desktop immediately
        if (cfg.HdtvDisplayDevicePath != null)
        {
            string? primary = _display.GetPrimaryMonitorDevicePath();
            if (primary != null && primary.Equals(
                    cfg.HdtvDisplayDevicePath, StringComparison.OrdinalIgnoreCase))
            {
                RevertNow(SwitchReason.Startup);
            }
        }

        return ValueTask.CompletedTask;
    }

    // ── Explicit switches ─────────────────────────────────────────────────────

    public SwitchResult ForwardSwitchNow(SwitchReason reason)
    {
        var cfg = _config.Current;

        if (cfg.HdtvDisplayDevicePath == null)
            return SwitchResult.DisplayNotFound;

        bool ok = _display.TrySetPrimaryMonitor(cfg.HdtvDisplayDevicePath, out string? displayErr);
        if (!ok)
        {
            var r = SwitchResult.Failed;
            _notifications.ShowSwitchNotification(SwitchDirection.Forward, reason, r);
            _notifications.ShowWarning($"Display switch failed: {displayErr}");
            RaiseSwitchCompleted(SwitchDirection.Forward, reason, r);
            return r;
        }

        var audioResult = SwitchAudio(cfg.HdtvAudioDeviceId, cfg);

        SetState(SwitcherState.HdtvActive);
        _notifications.ShowSwitchNotification(SwitchDirection.Forward, reason, audioResult);
        RaiseSwitchCompleted(SwitchDirection.Forward, reason, audioResult);
        return audioResult;
    }

    public SwitchResult RevertNow(SwitchReason reason)
    {
        var cfg = _config.Current;

        if (cfg.PreferredPrimaryDisplayDevicePath == null)
            return SwitchResult.DisplayNotFound;

        // No-op if the desktop monitor is already primary
        string? primary = _display.GetPrimaryMonitorDevicePath();
        if (primary != null && primary.Equals(
                cfg.PreferredPrimaryDisplayDevicePath, StringComparison.OrdinalIgnoreCase))
        {
            SetState(SwitcherState.DesktopIdle);
            return SwitchResult.NoActionNeeded;
        }

        bool ok = _display.TrySetPrimaryMonitor(cfg.PreferredPrimaryDisplayDevicePath, out string? displayErr);
        if (!ok)
        {
            var r = SwitchResult.Failed;
            _notifications.ShowSwitchNotification(SwitchDirection.Revert, reason, r);
            _notifications.ShowWarning($"Display switch failed: {displayErr}");
            RaiseSwitchCompleted(SwitchDirection.Revert, reason, r);
            return r;
        }

        var audioResult = SwitchAudio(ResolveDesktopAudioId(cfg), cfg);

        SetState(SwitcherState.DesktopIdle);
        _notifications.ShowSwitchNotification(SwitchDirection.Revert, reason, audioResult);
        RaiseSwitchCompleted(SwitchDirection.Revert, reason, audioResult);
        return audioResult;
    }

    // ── State machine (activity samples) ─────────────────────────────────────

    private void OnSampleProduced(object? sender, ActivitySample sample)
    {
        SwitcherState?  toState    = null;
        SwitchDirection? switchDir = null;
        SwitchReason?    switchReason = null;
        bool tvShow;

        lock (_lock)
        {
            var cfg  = _config.Current;
            var zone = sample.EffectiveZone;
            var now  = sample.Timestamp;
            tvShow   = _tvShowMode;

            switch (_state)
            {
                // ── Desktop: waiting for HDTV activity ────────────────────────
                case SwitcherState.DesktopIdle:
                    if (zone == ActivityZone.Hdtv)
                    {
                        _dwellStart = now;
                        toState = SwitcherState.DesktopHdtvDwelling;
                    }
                    break;

                // ── Desktop: sustained HDTV activity will trigger forward ──────
                case SwitcherState.DesktopHdtvDwelling:
                    if (zone == ActivityZone.Desktop || zone == ActivityZone.None)
                    {
                        toState = SwitcherState.DesktopIdle;
                    }
                    else if ((now - _dwellStart).TotalSeconds >= HdtvDwellThreshold(cfg))
                    {
                        switchDir    = SwitchDirection.Forward;
                        switchReason = SwitchReason.ExclusiveHdtvActivity;
                    }
                    break;

                // ── HDTV is primary: track activity, watch for idle / desktop ──
                case SwitcherState.HdtvActive:
                    if (zone != ActivityZone.None)
                    {
                        _dwellStart = now; // last-seen-activity timestamp
                    }
                    else if (!tvShow &&
                             (now - _dwellStart).TotalSeconds >= cfg.IdleTimeoutSeconds)
                    {
                        _dwellStart = now; // countdown start
                        toState = SwitcherState.HdtvIdleCountdown;
                    }

                    if (zone == ActivityZone.Desktop)
                    {
                        _dwellStart = now;
                        toState = SwitcherState.HdtvDesktopDwelling;
                    }
                    break;

                // ── HDTV idle: user has gone quiet, countdown to revert ────────
                case SwitcherState.HdtvIdleCountdown:
                    if (zone != ActivityZone.None)
                    {
                        _dwellStart = now;
                        toState = SwitcherState.HdtvActive;
                    }
                    else if ((now - _dwellStart).TotalSeconds >= cfg.DesktopDwellSeconds)
                    {
                        switchDir    = SwitchDirection.Revert;
                        switchReason = SwitchReason.IdleTimeout;
                    }
                    break;

                // ── HDTV active: sustained desktop activity will trigger revert ─
                case SwitcherState.HdtvDesktopDwelling:
                    if (zone == ActivityZone.Hdtv)
                    {
                        _dwellStart = now;
                        toState = SwitcherState.HdtvActive;
                    }
                    else if ((now - _dwellStart).TotalSeconds >= cfg.DesktopDwellSeconds)
                    {
                        switchDir    = SwitchDirection.Revert;
                        switchReason = SwitchReason.ExclusiveDesktopActivity;
                    }
                    break;
            }

            if (toState.HasValue)
                _state = toState.Value;
        }

        if (toState.HasValue)
            StateChanged?.Invoke(this, toState.Value);

        // Perform switch outside the lock (COM calls can be slow)
        if (switchDir.HasValue && switchReason.HasValue)
        {
            if (switchDir.Value == SwitchDirection.Forward)
                ForwardSwitchNow(switchReason.Value);
            else
                RevertNow(switchReason.Value);
        }
    }

    // ── Configuration change ──────────────────────────────────────────────────

    private void OnConfigurationChanged(object? sender, AppConfiguration cfg)
    {
        // Restart the WinEvent hook whenever the designated HDTV monitor changes
        // so window-drag detection tracks the correct monitor immediately.
        _windowEvents.Stop();

        if (cfg.HdtvDisplayDevicePath != null)
        {
            var hdtv = _display.GetActiveMonitors()
                .FirstOrDefault(m => m.DevicePath.Equals(
                    cfg.HdtvDisplayDevicePath, StringComparison.OrdinalIgnoreCase));
            if (hdtv != null)
                _windowEvents.Start(hdtv);
        }
    }

    // ── Window-drag event (UI thread) ─────────────────────────────────────────

    private void OnWindowMovedToHdtv(object? sender, EventArgs e)
    {
        // Per spec §5.8: a window move to HDTV does not directly trigger a forward
        // switch — it signals likely intent and warrants faster sampling. Let the
        // activity-dwell state machine (§5.7) make the actual switch decision.
        _activity.ActivateElevatedPolling();
    }

    // ── Audio helper ──────────────────────────────────────────────────────────

    private SwitchResult SwitchAudio(string? targetDeviceId, AppConfiguration cfg)
    {
        if (!cfg.AudioSwitchingEnabled || targetDeviceId == null)
            return SwitchResult.Success;

        // RespectManualAudioOverride: if the user manually changed audio away from
        // what we last set, leave it alone.
        if (cfg.RespectManualAudioOverride && _lastSetAudioId != null)
        {
            string? current = _audio.GetDefaultPlaybackDeviceId();
            if (!string.Equals(current, _lastSetAudioId, StringComparison.OrdinalIgnoreCase))
                return SwitchResult.Success; // user override in effect
        }

        bool ok = _audio.TrySetDefaultPlaybackDevice(targetDeviceId, out string? err);
        if (ok)
        {
            _lastSetAudioId = targetDeviceId;
            return SwitchResult.Success;
        }

        _notifications.ShowWarning($"Audio switch failed: {err}");
        return SwitchResult.AudioFailed;
    }

    private string? ResolveDesktopAudioId(AppConfiguration cfg)
    {
        if (cfg.DesktopAudioDeviceId != null)
            return cfg.DesktopAudioDeviceId;

        if (cfg.PreferredPrimaryDisplayDevicePath == null)
            return null;

        var desktop = _display.GetActiveMonitors()
            .FirstOrDefault(m => m.DevicePath.Equals(
                cfg.PreferredPrimaryDisplayDevicePath, StringComparison.OrdinalIgnoreCase));

        return desktop != null ? _audio.AutoDetectEndpointForMonitor(desktop)?.DeviceId : null;
    }

    // ── Misc helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// When elevated polling is active the HDTV dwell threshold scales down by the
    /// same ratio as the poll interval, so the forward switch fires proportionally sooner.
    /// e.g. defaults: 60s × (1s / 5s) = 12s during elevated polling.
    /// </summary>
    private double HdtvDwellThreshold(AppConfiguration cfg)
    {
        if (!_activity.IsElevatedPollingActive || cfg.PollIntervalSeconds <= 0)
            return cfg.HdtvDwellSeconds;

        double ratio = (double)cfg.ElevatedPollIntervalSeconds / cfg.PollIntervalSeconds;
        return cfg.HdtvDwellSeconds * ratio;
    }

    private void SetState(SwitcherState s)
    {
        lock (_lock)
        {
            _state = s;
            _dwellStart = DateTimeOffset.UtcNow;
        }
        StateChanged?.Invoke(this, s);
    }

    private void RaiseSwitchCompleted(SwitchDirection dir, SwitchReason reason, SwitchResult result) =>
        SwitchCompleted?.Invoke(this, (dir, reason, result));

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        _activity.SampleProduced        -= OnSampleProduced;
        _windowEvents.WindowMovedToHdtv -= OnWindowMovedToHdtv;
        _config.ConfigurationChanged    -= OnConfigurationChanged;
        _activity.Stop();
        _windowEvents.Stop();
    }
}
