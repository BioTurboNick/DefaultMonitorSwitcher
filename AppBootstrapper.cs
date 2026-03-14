using DefaultMonitorSwitcher.Core;
using DefaultMonitorSwitcher.Infrastructure.Display;
using DefaultMonitorSwitcher.Infrastructure.Input;
using DefaultMonitorSwitcher.Services;

namespace DefaultMonitorSwitcher;

public sealed class AppBootstrapper : IDisposable
{
    private readonly DisplayService       _displayService = new();
    private readonly ConfigurationService _configService  = new();
    private ActivityTracker?   _tracker;
    private WindowEventSource? _windowEventSource;

    private const string HdtvPath =
        @"\\?\DISPLAY#SAM7202#5&1470b2ba&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    public void Start()
    {
        _configService.Save(_configService.Current with
        {
            HdtvDisplayDevicePath = HdtvPath,
            MouseDwellSeconds     = 0,
            PollIntervalSeconds   = 1,
        });

        // WindowEventSource MUST be started on the UI thread
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        var monitors   = _displayService.GetActiveMonitors();
        var hdtv       = monitors.FirstOrDefault(m => m.DevicePath == HdtvPath);

        _windowEventSource = new WindowEventSource(_displayService, dispatcher);
        if (hdtv != null)
        {
            dispatcher.Invoke(() => _windowEventSource.Start(hdtv));
            _windowEventSource.WindowMovedToHdtv += (_, _) =>
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.ff}]  *** WindowMovedToHdtv fired ***");
        }
        else
        {
            Console.WriteLine("HDTV monitor not found — WindowEventSource not started.");
        }

        _tracker = new ActivityTracker(_displayService, _configService);
        _tracker.SampleProduced += OnSample;
        _tracker.Start();

        dispatcher.InvokeAsync(() =>
        {
            System.Windows.MessageBox.Show(
                "Activity tracker + window-move hook running.\n\n" +
                "• Move mouse / focus windows across monitors → see zone output\n" +
                "• Move or Win+Shift+Arrow a window to the TV → see WindowMovedToHdtv\n\n" +
                "Click OK to stop.",
                "DefaultMonitorSwitcher — Stage 4: Window Event Source",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);

            System.Windows.Application.Current.Shutdown();
        });
    }

    private static void OnSample(object? sender, ActivitySample sample)
    {
        Console.WriteLine(
            $"[{sample.Timestamp:HH:mm:ss.ff}]  " +
            $"Cursor={sample.CursorZone,-8}  " +
            $"FgWindow={sample.ForegroundWindowZone,-8}  " +
            $"Effective={sample.EffectiveZone}");
    }

    public void OnSessionEnding() { }

    public void Dispose()
    {
        _tracker?.Stop();
        _tracker?.Dispose();
        _windowEventSource?.Stop();
        _windowEventSource?.Dispose();
    }
}
