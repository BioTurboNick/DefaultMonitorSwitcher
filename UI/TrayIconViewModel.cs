using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DefaultMonitorSwitcher.Core;
using DefaultMonitorSwitcher.UI.Settings;

namespace DefaultMonitorSwitcher.UI;

public sealed partial class TrayIconViewModel : ObservableObject, IDisposable
{
    private readonly ISwitchController      _controller;
    private readonly Func<SettingsViewModel> _settingsVmFactory;

    [ObservableProperty] private TrayIconState _iconState   = TrayIconState.Neutral;
    [ObservableProperty] private string _statusText         = "Desktop primary";
    [ObservableProperty] private bool _tvShowModeEnabled;
    [ObservableProperty] private bool _isHdtvPrimary;

    public TrayIconViewModel(
        ISwitchController       controller,
        IConfigurationService   _,
        Func<SettingsViewModel> settingsVmFactory)
    {
        _controller        = controller;
        _settingsVmFactory = settingsVmFactory;

        _tvShowModeEnabled = controller.TvShowModeEnabled;
        UpdateFromState(controller.CurrentState);

        controller.StateChanged    += OnStateChanged;
        controller.SwitchCompleted += OnSwitchCompleted;
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(IsHdtvPrimary))]
    private void RevertNow() => _controller.RevertNow(SwitchReason.Manual);

    [RelayCommand]
    private void ToggleTvShowMode()
    {
        TvShowModeEnabled = !TvShowModeEnabled;
        _controller.TvShowModeEnabled = TvShowModeEnabled;
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var vm  = _settingsVmFactory();
        var win = new SettingsWindow(vm);
        win.Show();
    }

    [RelayCommand]
    private void Exit() => System.Windows.Application.Current.Shutdown();

    // ── State updates ─────────────────────────────────────────────────────────

    private void OnStateChanged(object? sender, SwitcherState state)
    {
        System.Windows.Application.Current.Dispatcher.InvokeAsync(() => UpdateFromState(state));
    }

    private void OnSwitchCompleted(
        object? sender,
        (SwitchDirection Direction, SwitchReason Reason, SwitchResult Result) _) { }

    partial void OnIsHdtvPrimaryChanged(bool value) =>
        RevertNowCommand.NotifyCanExecuteChanged();

    partial void OnTvShowModeEnabledChanged(bool value) =>
        UpdateFromState(_controller.CurrentState);

    private void UpdateFromState(SwitcherState state)
    {
        IsHdtvPrimary = state is SwitcherState.HdtvActive
                               or SwitcherState.HdtvIdleCountdown
                               or SwitcherState.HdtvDesktopDwelling;

        IconState = state switch
        {
            SwitcherState.HdtvActive         => TvShowModeEnabled ? TrayIconState.TvShowMode : TrayIconState.Active,
            SwitcherState.HdtvIdleCountdown  => TrayIconState.IdleCountdown,
            _                                => TrayIconState.Neutral,
        };

        StatusText = state switch
        {
            SwitcherState.DesktopIdle          => "Desktop primary",
            SwitcherState.DesktopHdtvDwelling  => "Switching to HDTV\u2026",
            SwitcherState.HdtvActive           => TvShowModeEnabled ? "HDTV (TV Show Mode)" : "HDTV primary",
            SwitcherState.HdtvIdleCountdown    => "HDTV \u2014 idle, reverting\u2026",
            SwitcherState.HdtvDesktopDwelling  => "HDTV \u2014 switching to desktop\u2026",
            _                                  => "Unknown",
        };
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        _controller.StateChanged    -= OnStateChanged;
        _controller.SwitchCompleted -= OnSwitchCompleted;
    }
}
