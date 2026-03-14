namespace DefaultMonitorSwitcher.Core;

public enum ActivityZone { None, Desktop, Hdtv }

public enum SwitcherState
{
    DesktopIdle,
    DesktopHdtvDwelling,
    HdtvActive,
    HdtvIdleCountdown,
    HdtvDesktopDwelling,
}

public enum SwitchDirection { Forward, Revert }

public enum SwitchReason
{
    IdleTimeout,
    ExclusiveDesktopActivity,
    SessionEnding,
    Startup,
    Manual,
    ExclusiveHdtvActivity,
}

public enum SwitchResult { Success, NoActionNeeded, DisplayNotFound, AudioFailed, Failed }

public enum TrayIconState { Neutral, Active, IdleCountdown, TvShowMode }
