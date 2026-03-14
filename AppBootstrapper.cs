using DefaultMonitorSwitcher.Core;
using DefaultMonitorSwitcher.Infrastructure.Audio;
using DefaultMonitorSwitcher.Infrastructure.Display;
using DefaultMonitorSwitcher.Infrastructure.Input;
using DefaultMonitorSwitcher.Services;
using DefaultMonitorSwitcher.UI;
using DefaultMonitorSwitcher.UI.Settings;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.DependencyInjection;

namespace DefaultMonitorSwitcher;

public sealed class AppBootstrapper : IDisposable
{
    private ServiceProvider? _services;
    private TaskbarIcon?      _trayIcon;

    public void Start()
    {
        // ── Build DI container ────────────────────────────────────────────
        var sc = new ServiceCollection();
        RegisterServices(sc);
        _services = sc.BuildServiceProvider();

        // ── Load configuration ────────────────────────────────────────────
        var config = _services.GetRequiredService<IConfigurationService>();
        config.InitializeAsync().AsTask().GetAwaiter().GetResult();

        // ── Wire tray icon ────────────────────────────────────────────────
        _trayIcon = (TaskbarIcon)System.Windows.Application.Current.Resources["TrayIcon"];

        var trayVm = _services.GetRequiredService<TrayIconViewModel>();
        _trayIcon.DataContext = trayVm;
        if (_trayIcon.ContextMenu is { } menu)
            menu.DataContext = trayVm;

        // Load icon from embedded WPF resource; fall back to system default
        var iconUri    = new Uri("pack://application:,,,/UI/Resources/Icons/app.ico");
        var iconStream = System.Windows.Application.GetResourceStream(iconUri)?.Stream;
        _trayIcon.Icon = iconStream != null
            ? new System.Drawing.Icon(iconStream)
            : System.Drawing.SystemIcons.Application;

        // Icon must be set before making the tray icon visible
        _trayIcon.Visibility = System.Windows.Visibility.Visible;

        // ── Attach notifications to tray ──────────────────────────────────
        var notif = (NotificationService)_services.GetRequiredService<INotificationService>();
        notif.Attach(_trayIcon);

        // ── Start controller (must be on UI thread — WinEventHook) ────────
        _services.GetRequiredService<ISwitchController>()
                 .StartAsync()
                 .AsTask().GetAwaiter().GetResult();

        // ── Honour RunOnStartup setting ────────────────────────────────────
        var startup = _services.GetRequiredService<IStartupService>();
        if (config.Current.RunOnStartup && !startup.IsRegistered) startup.Register();
        if (!config.Current.RunOnStartup &&  startup.IsRegistered) startup.Unregister();
    }

    public void OnSessionEnding()
    {
        _services?.GetRequiredService<ISwitchController>()
                  .RevertNow(SwitchReason.SessionEnding);
    }

    public void Dispose()
    {
        _services?.GetRequiredService<TrayIconViewModel>().Dispose();
        _services?.GetRequiredService<ISwitchController>().Dispose();
        _trayIcon?.Dispose();
        _services?.Dispose();
    }

    // ── Service registration ──────────────────────────────────────────────────

    private static void RegisterServices(IServiceCollection sc)
    {
        // Infrastructure
        sc.AddSingleton<IDisplayService, DisplayService>();
        sc.AddSingleton<IAudioService,   AudioService>();
        sc.AddSingleton<IWindowEventSource>(sp => new WindowEventSource(
            sp.GetRequiredService<IDisplayService>(),
            System.Windows.Application.Current.Dispatcher));

        // Services
        sc.AddSingleton<IConfigurationService, ConfigurationService>();
        sc.AddSingleton<INotificationService,  NotificationService>();
        sc.AddSingleton<IStartupService,       StartupService>();
        sc.AddSingleton<IActivityTracker,      ActivityTracker>();
        sc.AddSingleton<ISwitchController,     SwitchController>();

        // ViewModels
        sc.AddSingleton(sp => new TrayIconViewModel(
            sp.GetRequiredService<ISwitchController>(),
            sp.GetRequiredService<IConfigurationService>(),
            () => sp.GetRequiredService<SettingsViewModel>()));

        sc.AddTransient(sp => new SettingsViewModel(
            sp.GetRequiredService<IDisplayService>(),
            sp.GetRequiredService<IAudioService>(),
            sp.GetRequiredService<IConfigurationService>(),
            sp.GetRequiredService<IStartupService>(),
            sp.GetRequiredService<ISwitchController>()));
    }
}
