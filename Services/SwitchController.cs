using DefaultMonitorSwitcher.Core;

namespace DefaultMonitorSwitcher.Services;

public sealed class SwitchController : ISwitchController
{
    public SwitcherState CurrentState { get; private set; } = SwitcherState.DesktopIdle;
    public bool TvShowModeEnabled { get; set; }
    public event EventHandler<SwitcherState>? StateChanged;
    public event EventHandler<(SwitchDirection, SwitchReason, SwitchResult)>? SwitchCompleted;
    public ValueTask StartAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
    public SwitchResult RevertNow(SwitchReason reason) => SwitchResult.NoActionNeeded;
    public SwitchResult ForwardSwitchNow(SwitchReason reason) => SwitchResult.NoActionNeeded;
    public void Dispose() { }
}
