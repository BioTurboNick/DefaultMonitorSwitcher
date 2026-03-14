using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DefaultMonitorSwitcher.Core;

namespace DefaultMonitorSwitcher.UI.Settings;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IDisplayService      _display;
    private readonly IAudioService        _audio;
    private readonly IConfigurationService _config;
    private readonly IStartupService      _startup;
    private readonly ISwitchController    _controller;

    // ── Collections ───────────────────────────────────────────────────────────

    public ObservableCollection<MonitorInfo>       AvailableMonitors  { get; } = new();
    public ObservableCollection<AudioEndpointInfo> AvailableEndpoints { get; } = new();

    // ── Display ───────────────────────────────────────────────────────────────

    [ObservableProperty] private MonitorInfo? _selectedHdtvMonitor;
    [ObservableProperty] private MonitorInfo? _selectedDesktopPrimaryMonitor;

    // ── Audio ─────────────────────────────────────────────────────────────────

    [ObservableProperty] private AudioEndpointInfo? _selectedHdtvAudioEndpoint;
    [ObservableProperty] private AudioEndpointInfo? _selectedDesktopAudioEndpoint;
    [ObservableProperty] private bool _audioSwitchingEnabled;
    [ObservableProperty] private bool _respectManualAudioOverride;

    // ── Timings ───────────────────────────────────────────────────────────────

    [ObservableProperty] private int _idleTimeoutSeconds;
    [ObservableProperty] private int _desktopDwellSeconds;
    [ObservableProperty] private int _hdtvDwellSeconds;
    [ObservableProperty] private int _mouseDwellSeconds;
    [ObservableProperty] private int _pollIntervalSeconds;
    [ObservableProperty] private int _elevatedPollIntervalSeconds;
    [ObservableProperty] private int _elevatedPollDurationSeconds;

    // ── Misc ──────────────────────────────────────────────────────────────────

    [ObservableProperty] private bool _tvShowModeEnabled;
    [ObservableProperty] private bool _runOnStartup;

    // ── Close event (raised by Save/Cancel) ───────────────────────────────────

    public event EventHandler? CloseRequested;

    // ── Constructor ───────────────────────────────────────────────────────────

    public SettingsViewModel(
        IDisplayService       display,
        IAudioService         audio,
        IConfigurationService config,
        IStartupService       startup,
        ISwitchController     controller)
    {
        _display    = display;
        _audio      = audio;
        _config     = config;
        _startup    = startup;
        _controller = controller;

        Load();
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void RefreshDevices() => Load();

    [RelayCommand]
    private void Save()
    {
        var cfg = new AppConfiguration
        {
            HdtvDisplayDevicePath              = SelectedHdtvMonitor?.DevicePath,
            PreferredPrimaryDisplayDevicePath  = SelectedDesktopPrimaryMonitor?.DevicePath,
            HdtvAudioDeviceId                  = SelectedHdtvAudioEndpoint?.DeviceId,
            DesktopAudioDeviceId               = SelectedDesktopAudioEndpoint?.DeviceId,
            AudioSwitchingEnabled              = AudioSwitchingEnabled,
            RespectManualAudioOverride         = RespectManualAudioOverride,
            IdleTimeoutSeconds                 = IdleTimeoutSeconds,
            DesktopDwellSeconds                = DesktopDwellSeconds,
            HdtvDwellSeconds                   = HdtvDwellSeconds,
            MouseDwellSeconds                  = MouseDwellSeconds,
            PollIntervalSeconds                = PollIntervalSeconds,
            ElevatedPollIntervalSeconds        = ElevatedPollIntervalSeconds,
            ElevatedPollDurationSeconds        = ElevatedPollDurationSeconds,
            TvShowModeEnabled                  = TvShowModeEnabled,
            RunOnStartup                       = RunOnStartup,
        };

        _config.Save(cfg);
        _controller.TvShowModeEnabled = TvShowModeEnabled;

        if (RunOnStartup) _startup.Register();
        else              _startup.Unregister();

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void Load()
    {
        var cfg = _config.Current;

        AvailableMonitors.Clear();
        foreach (var m in _display.GetActiveMonitors())
            AvailableMonitors.Add(m);

        AvailableEndpoints.Clear();
        foreach (var e in _audio.GetPlaybackEndpoints())
            AvailableEndpoints.Add(e);

        SelectedHdtvMonitor = AvailableMonitors
            .FirstOrDefault(m => m.DevicePath == cfg.HdtvDisplayDevicePath);
        SelectedDesktopPrimaryMonitor = AvailableMonitors
            .FirstOrDefault(m => m.DevicePath == cfg.PreferredPrimaryDisplayDevicePath);

        SelectedHdtvAudioEndpoint = AvailableEndpoints
            .FirstOrDefault(e => e.DeviceId == cfg.HdtvAudioDeviceId);
        SelectedDesktopAudioEndpoint = AvailableEndpoints
            .FirstOrDefault(e => e.DeviceId == cfg.DesktopAudioDeviceId);

        AudioSwitchingEnabled        = cfg.AudioSwitchingEnabled;
        RespectManualAudioOverride   = cfg.RespectManualAudioOverride;
        IdleTimeoutSeconds           = cfg.IdleTimeoutSeconds;
        DesktopDwellSeconds          = cfg.DesktopDwellSeconds;
        HdtvDwellSeconds             = cfg.HdtvDwellSeconds;
        MouseDwellSeconds            = cfg.MouseDwellSeconds;
        PollIntervalSeconds          = cfg.PollIntervalSeconds;
        ElevatedPollIntervalSeconds  = cfg.ElevatedPollIntervalSeconds;
        ElevatedPollDurationSeconds  = cfg.ElevatedPollDurationSeconds;
        TvShowModeEnabled            = _controller.TvShowModeEnabled;
        RunOnStartup                 = _startup.IsRegistered;
    }
}
