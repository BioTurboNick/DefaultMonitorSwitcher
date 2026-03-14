using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DefaultMonitorSwitcher.Core;

namespace DefaultMonitorSwitcher.UI.Settings;

public sealed partial class SettingsViewModel : ObservableObject
{
    // Display
    public ObservableCollection<MonitorInfo> AvailableMonitors { get; } = new();
    [ObservableProperty] private MonitorInfo? _selectedHdtvMonitor;
    [ObservableProperty] private MonitorInfo? _selectedDesktopPrimaryMonitor;

    // Audio
    public ObservableCollection<AudioEndpointInfo> AvailableEndpoints { get; } = new();
    [ObservableProperty] private AudioEndpointInfo? _selectedHdtvAudioEndpoint;
    [ObservableProperty] private AudioEndpointInfo? _selectedDesktopAudioEndpoint;
    [ObservableProperty] private string? _autoDetectedHdtvAudioName;
    [ObservableProperty] private string? _autoDetectedDesktopAudioName;
    [ObservableProperty] private bool _audioSwitchingEnabled;
    [ObservableProperty] private bool _respectManualAudioOverride;

    // Thresholds
    [ObservableProperty] private int _idleTimeoutSeconds;
    [ObservableProperty] private int _desktopDwellSeconds;
    [ObservableProperty] private int _hdtvDwellSeconds;
    [ObservableProperty] private int _mouseDwellSeconds;
    [ObservableProperty] private int _pollIntervalSeconds;
    [ObservableProperty] private int _elevatedPollIntervalSeconds;
    [ObservableProperty] private int _elevatedPollDurationSeconds;

    // Misc
    [ObservableProperty] private bool _tvShowModeEnabled;
    [ObservableProperty] private bool _runOnStartup;

    [RelayCommand] private void Save() { }
    [RelayCommand] private void Cancel() { }
    [RelayCommand] private void RefreshDevices() { }
}
