using DefaultMonitorSwitcher.Core;
using Hardcodet.Wpf.TaskbarNotification;

namespace DefaultMonitorSwitcher.Services;

public sealed class NotificationService : INotificationService
{
    private TaskbarIcon? _trayIcon;

    /// <summary>Called by AppBootstrapper after the tray icon is created.</summary>
    public void Attach(TaskbarIcon trayIcon) => _trayIcon = trayIcon;

    public void ShowSwitchNotification(SwitchDirection direction, SwitchReason reason, SwitchResult result)
    {
        if (_trayIcon == null) return;

        string title = direction == SwitchDirection.Forward
            ? "Switched to HDTV"
            : "Switched to Desktop";

        string body = reason switch
        {
            SwitchReason.ExclusiveHdtvActivity    => "Activity detected on HDTV.",
            SwitchReason.ExclusiveDesktopActivity => "Activity detected on desktop.",
            SwitchReason.IdleTimeout              => "System idle — reverting to desktop.",
            SwitchReason.Manual                   => "Manually reverted.",
            SwitchReason.Startup                  => "Reverted at startup.",
            SwitchReason.SessionEnding            => "Session ending — reverting.",
            _                                     => reason.ToString(),
        };

        if (result == SwitchResult.AudioFailed)
            body += " (audio switch failed)";

        _trayIcon.ShowBalloonTip(title, body, BalloonIcon.Info);
    }

    public void ShowWarning(string message)
    {
        _trayIcon?.ShowBalloonTip("DefaultMonitorSwitcher", message, BalloonIcon.Warning);
    }
}
