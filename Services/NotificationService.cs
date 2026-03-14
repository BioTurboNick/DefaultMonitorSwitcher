using DefaultMonitorSwitcher.Core;

namespace DefaultMonitorSwitcher.Services;

public sealed class NotificationService : INotificationService
{
    public void ShowSwitchNotification(SwitchDirection direction, SwitchReason reason, SwitchResult result) { }
    public void ShowWarning(string message) { }
}
