using DefaultMonitorSwitcher.Core;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace DefaultMonitorSwitcher.Services;

public sealed class ActivityTracker : IActivityTracker
{
    private readonly IDisplayService _displayService;
    private readonly IConfigurationService _configService;

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    // Elevated polling state (lock-protected)
    private readonly Lock _elevatedLock = new();
    private DateTimeOffset? _elevatedExpiry;

    // Mouse-dwell smoothing: tracks when the cursor first entered each monitor zone
    // Key = DevicePath, Value = time cursor entered that monitor
    private readonly Dictionary<string, DateTimeOffset> _cursorZoneEntry = new();
    private string? _currentCursorDevicePath;

    // Session lock state — set from SystemEvents, read from poll thread
    private volatile bool _isLocked;

    public ActivityTracker(IDisplayService displayService, IConfigurationService configService)
    {
        _displayService = displayService;
        _configService  = configService;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
            _isLocked = true;
        else if (e.Reason == SessionSwitchReason.SessionUnlock)
            _isLocked = false;
    }

    public event EventHandler<ActivitySample>? SampleProduced;

    public bool IsElevatedPollingActive
    {
        get { lock (_elevatedLock) return _elevatedExpiry.HasValue && DateTimeOffset.UtcNow < _elevatedExpiry.Value; }
    }

    public void ActivateElevatedPolling()
    {
        lock (_elevatedLock)
        {
            var cfg = _configService.Current;
            _elevatedExpiry = DateTimeOffset.UtcNow.AddSeconds(cfg.ElevatedPollDurationSeconds);
        }
        // Restart the loop so the new interval takes effect immediately
        RestartLoop();
    }

    public void Start()
    {
        if (_loopTask != null)
            return;
        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => PollLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _loopTask?.Wait(TimeSpan.FromSeconds(3));
        _cts?.Dispose();
        _cts = null;
        _loopTask = null;
    }

    public void Dispose()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        Stop();
    }

    // ── Internal ─────────────────────────────────────────────────────────────

    private void RestartLoop()
    {
        if (_cts == null)
            return;
        _cts.Cancel();
        _loopTask?.Wait(TimeSpan.FromSeconds(3));
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => PollLoopAsync(_cts.Token));
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var cfg      = _configService.Current;
            var interval = GetCurrentInterval(cfg);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(interval), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (ct.IsCancellationRequested)
                return;

            var monitors = _displayService.GetActiveMonitors();
            if (monitors.Count == 0)
                continue;

            var now         = DateTimeOffset.UtcNow;
            var cursorZone  = GetCursorZone(monitors, cfg, now);
            var windowZone  = GetForegroundWindowZone(monitors);

            var sample = new ActivitySample
            {
                Timestamp            = now,
                CursorZone           = cursorZone,
                ForegroundWindowZone = windowZone,
            };

            SampleProduced?.Invoke(this, sample);

            // Check if elevated period expired; if so, next iteration will use normal interval
            lock (_elevatedLock)
            {
                if (_elevatedExpiry.HasValue && DateTimeOffset.UtcNow >= _elevatedExpiry.Value)
                    _elevatedExpiry = null;
            }
        }
    }

    private int GetCurrentInterval(AppConfiguration cfg)
    {
        lock (_elevatedLock)
        {
            if (_elevatedExpiry.HasValue && DateTimeOffset.UtcNow < _elevatedExpiry.Value)
                return cfg.ElevatedPollIntervalSeconds;
        }
        return cfg.PollIntervalSeconds;
    }

    private ActivityZone GetCursorZone(
        IReadOnlyList<MonitorInfo> monitors,
        AppConfiguration cfg,
        DateTimeOffset now)
    {
        if (_isLocked)
            return ActivityZone.None;

        if (!PInvoke.GetCursorPos(out var pt))
            return ActivityZone.None;

        var cursorPoint = new System.Drawing.Point(pt.X, pt.Y);
        var monitor     = monitors.FirstOrDefault(m => m.Bounds.Contains(cursorPoint));

        if (monitor == null)
        {
            _currentCursorDevicePath = null;
            return ActivityZone.None;
        }

        // Mouse-dwell smoothing: the cursor must remain on a monitor for
        // MouseDwellSeconds before it's counted as a signal for that monitor.
        if (_currentCursorDevicePath != monitor.DevicePath)
        {
            // Cursor just moved to a new monitor; record entry time
            if (!_cursorZoneEntry.ContainsKey(monitor.DevicePath))
                _cursorZoneEntry[monitor.DevicePath] = now;
            _currentCursorDevicePath = monitor.DevicePath;
        }

        var entryTime = _cursorZoneEntry.GetValueOrDefault(monitor.DevicePath, now);
        var dwellElapsed = (now - entryTime).TotalSeconds;

        if (dwellElapsed < cfg.MouseDwellSeconds)
            return ActivityZone.None; // dwell not yet satisfied

        // Clean up stale entries for other monitors
        foreach (var key in _cursorZoneEntry.Keys
            .Where(k => k != monitor.DevicePath).ToList())
            _cursorZoneEntry.Remove(key);

        return ClassifyMonitor(monitor, cfg);
    }

    private ActivityZone GetForegroundWindowZone(IReadOnlyList<MonitorInfo> monitors)
    {
        if (_isLocked)
            return ActivityZone.None;

        var hwnd = PInvoke.GetForegroundWindow();
        if (hwnd == default)
            return ActivityZone.None;

        var monitor = _displayService.MonitorFromWindowHandle((nint)(IntPtr)hwnd);
        return monitor == null ? ActivityZone.None : ClassifyMonitor(monitor, _configService.Current);
    }

    private static ActivityZone ClassifyMonitor(MonitorInfo monitor, AppConfiguration cfg)
    {
        if (cfg.HdtvDisplayDevicePath != null &&
            monitor.DevicePath.Equals(cfg.HdtvDisplayDevicePath, StringComparison.OrdinalIgnoreCase))
            return ActivityZone.Hdtv;

        return ActivityZone.Desktop;
    }
}
