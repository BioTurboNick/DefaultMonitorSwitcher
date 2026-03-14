namespace DefaultMonitorSwitcher.Core;

public interface ISwitchController : IDisposable
{
    SwitcherState CurrentState { get; }
    bool TvShowModeEnabled { get; set; }

    event EventHandler<SwitcherState>? StateChanged;
    event EventHandler<(SwitchDirection Direction, SwitchReason Reason, SwitchResult Result)>? SwitchCompleted;

    /// <summary>
    /// Subscribes to activity and window-move events and begins monitoring.
    /// Performs a startup revert if the HDTV is already primary.
    /// </summary>
    ValueTask StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Immediate synchronous revert. Safe to call from WM_ENDSESSION.
    /// No-op if a desktop monitor is already primary.
    /// </summary>
    SwitchResult RevertNow(SwitchReason reason);

    SwitchResult ForwardSwitchNow(SwitchReason reason);
}
