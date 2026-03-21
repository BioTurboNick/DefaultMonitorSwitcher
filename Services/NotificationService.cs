using System.IO;
using DefaultMonitorSwitcher.Core;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Win32;
using Windows.UI.Notifications;

namespace DefaultMonitorSwitcher.Services;

public sealed class NotificationService : INotificationService
{
    private const string AppId    = "DefaultMonitorSwitcher";
    private const string ToastTag = "switch";

    private TaskbarIcon? _trayIcon;

    /// <summary>Called by AppBootstrapper after the tray icon is created.</summary>
    public void Attach(TaskbarIcon trayIcon)
    {
        _trayIcon = trayIcon;
        EnsureAumidRegistered();
    }

    public void ShowSwitchNotification(SwitchDirection direction, SwitchReason reason, SwitchResult result)
    {
        string title = direction == SwitchDirection.Forward
            ? "Switched to HDTV"
            : "Switched to Desktop";

        string body = (direction, reason) switch
        {
            (SwitchDirection.Revert,  SwitchReason.IdleTimeout)              => "No HDTV activity — reverting to desktop.",
            (SwitchDirection.Revert,  SwitchReason.ExclusiveDesktopActivity) => "Activity returned to desk monitors.",
            (SwitchDirection.Revert,  SwitchReason.SessionEnding)            => "Session ending — reverting to desktop.",
            (SwitchDirection.Revert,  SwitchReason.SessionLocked)            => "Workstation locked — reverting to desktop.",
            (SwitchDirection.Revert,  SwitchReason.Startup)                  => "HDTV was primary at startup — reverting.",
            (SwitchDirection.Revert,  SwitchReason.Manual)                   => "Manually reverted to desktop.",
            (SwitchDirection.Forward, SwitchReason.ExclusiveHdtvActivity)    => "Exclusive HDTV activity detected.",
            (SwitchDirection.Forward, SwitchReason.Manual)                   => "Manually switched to HDTV.",
            _                                                                 => reason.ToString(),
        };

        if (result == SwitchResult.AudioFailed)
            body += " (audio switch failed)";

        ShowToast(title, body);
    }

    public void ShowWarning(string message) => ShowToast("DefaultMonitorSwitcher", message);

    // ── Toast helpers ─────────────────────────────────────────────────────────

    private void ShowToast(string title, string body)
    {
        try
        {
            var xml = new ToastContentBuilder()
                .AddText(title)
                .AddText(body)
                .GetToastContent()
                .GetXml();

            var notif = new ToastNotification(xml)
            {
                Tag            = ToastTag,
                ExpirationTime = DateTimeOffset.Now.AddMinutes(3),
            };

            ToastNotificationManager.CreateToastNotifier(AppId).Show(notif);
        }
        catch
        {
            // Fall back to tray balloon if UWP toast fails
            _trayIcon?.ShowBalloonTip(title, body, Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
        }
    }

    /// <summary>
    /// Registers a minimal AUMID entry under HKCU so Windows can attribute
    /// toast notifications to this app without MSIX packaging.
    /// </summary>
    private static void EnsureAumidRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                $@"Software\Classes\AppUserModelId\{AppId}");
            key.SetValue("DisplayName", AppId);
            if (GetOrCreateIconPng() is string pngPath)
                key.SetValue("IconUri", pngPath);
        }
        catch { }
    }

    /// <summary>
    /// Returns a path to a 256×256 PNG derived from the embedded app.ico.
    /// PNG is the correct format for AUMID IconUri — ICO causes Windows to pick
    /// an arbitrary frame and upscale it, resulting in a blurry icon.
    /// The PNG is written once to the app data directory and reused on subsequent runs.
    /// </summary>
    private static string? GetOrCreateIconPng()
    {
        try
        {
            var dir     = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DefaultMonitorSwitcher");
            Directory.CreateDirectory(dir);
            var pngPath = Path.Combine(dir, "app.png");
            if (!File.Exists(pngPath))
            {
                var sri = System.Windows.Application.GetResourceStream(
                    new Uri("pack://application:,,,/UI/Resources/Icons/app.ico"));
                using var icon = new System.Drawing.Icon(sri.Stream, new System.Drawing.Size(256, 256));
                using var bmp  = icon.ToBitmap();
                bmp.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
            }
            return pngPath;
        }
        catch { return null; }
    }

}
