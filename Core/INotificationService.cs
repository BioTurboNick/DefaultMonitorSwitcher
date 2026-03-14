namespace DefaultMonitorSwitcher.Core;

public interface INotificationService
{
    void ShowSwitchNotification(SwitchDirection direction, SwitchReason reason, SwitchResult result);
    void ShowWarning(string message);
}
