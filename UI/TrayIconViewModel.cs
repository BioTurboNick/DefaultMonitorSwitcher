using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DefaultMonitorSwitcher.Core;

namespace DefaultMonitorSwitcher.UI;

public sealed partial class TrayIconViewModel : ObservableObject, IDisposable
{
    [ObservableProperty] private TrayIconState _iconState = TrayIconState.Neutral;
    [ObservableProperty] private string _statusText = "Desktop primary";
    [ObservableProperty] private bool _tvShowModeEnabled;
    [ObservableProperty] private bool _isHdtvPrimary;

    [RelayCommand] private void RevertNow() { }
    [RelayCommand] private void ToggleTvShowMode() { }
    [RelayCommand] private void OpenSettings() { }
    [RelayCommand] private void Exit() { }

    public void Dispose() { }
}
