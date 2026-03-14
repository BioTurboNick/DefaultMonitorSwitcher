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

    public ObservableCollection<MonitorInfo> AvailableMonitors { get; } = new();

    // ── Display ───────────────────────────────────────────────────────────────

    [ObservableProperty] private MonitorInfo? _selectedHdtvMonitor;
    [ObservableProperty] private MonitorInfo? _selectedDesktopPrimaryMonitor;

    // ── Audio ─────────────────────────────────────────────────────────────────

    [ObservableProperty] private string _hdtvAudioLabel    = "Not detected";
    [ObservableProperty] private string _desktopAudioLabel = "Not detected";
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

    partial void OnSelectedHdtvMonitorChanged(MonitorInfo? value)
    {
        if (SelectedDesktopPrimaryMonitor == value || SelectedDesktopPrimaryMonitor == null)
            SelectedDesktopPrimaryMonitor = AvailableMonitors.FirstOrDefault(m => m != value);

        HdtvAudioLabel = value != null
            ? (_audio.AutoDetectEndpointForMonitor(value)?.FriendlyName ?? "Not detected")
            : "Not detected";
    }

    partial void OnSelectedDesktopPrimaryMonitorChanged(MonitorInfo? value)
    {
        DesktopAudioLabel = value != null
            ? (_audio.AutoDetectEndpointForMonitor(value)?.FriendlyName ?? "Not detected")
            : "Not detected";
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
            HdtvAudioDeviceId                  = SelectedHdtvMonitor != null
                                                     ? _audio.AutoDetectEndpointForMonitor(SelectedHdtvMonitor)?.DeviceId
                                                     : null,
            DesktopAudioDeviceId               = SelectedDesktopPrimaryMonitor != null
                                                     ? _audio.AutoDetectEndpointForMonitor(SelectedDesktopPrimaryMonitor)?.DeviceId
                                                     : null,
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

        SelectedHdtvMonitor = AvailableMonitors
            .FirstOrDefault(m => m.DevicePath == cfg.HdtvDisplayDevicePath);

        SelectedDesktopPrimaryMonitor = AvailableMonitors
            .FirstOrDefault(m => m.DevicePath == cfg.PreferredPrimaryDisplayDevicePath)
            ?? AvailableMonitors.FirstOrDefault(m => m != SelectedHdtvMonitor);

        // Labels are set by the OnSelected*Changed handlers above; set them here too
        // for the case where the selections didn't change (same value re-assigned).
        HdtvAudioLabel = SelectedHdtvMonitor != null
            ? (_audio.AutoDetectEndpointForMonitor(SelectedHdtvMonitor)?.FriendlyName ?? "Not detected")
            : "Not detected";
        DesktopAudioLabel = SelectedDesktopPrimaryMonitor != null
            ? (_audio.AutoDetectEndpointForMonitor(SelectedDesktopPrimaryMonitor)?.FriendlyName ?? "Not detected")
            : "Not detected";

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
