using DefaultMonitorSwitcher.Core;
using DefaultMonitorSwitcher.Infrastructure.Display;
using DefaultMonitorSwitcher.Services;

namespace DefaultMonitorSwitcher;

public sealed class AppBootstrapper : IDisposable
{
    private readonly DisplayService     _displayService = new();
    private readonly ConfigurationService _configService = new();
    private ActivityTracker?            _tracker;

    public void Start()
    {
        // Stage 3 debug: seed the HDTV device path detected in Stage 2
        _configService.Save(_configService.Current with
        {
            HdtvDisplayDevicePath =
                @"\\?\DISPLAY#SAM7202#5&1470b2ba&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}",
            // MouseDwellSeconds = 0 so we see instant cursor zone changes (no dwell wait)
            MouseDwellSeconds = 0,
            PollIntervalSeconds = 1,
        });

        _tracker = new ActivityTracker(_displayService, _configService);
        _tracker.SampleProduced += OnSample;
        _tracker.Start();

        System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            System.Windows.MessageBox.Show(
                "Activity tracker is running.\n\n" +
                "Move your mouse and focus windows across all three monitors.\n" +
                "Zone output is printed to the debug console.\n\n" +
                "Click OK to stop.",
                "DefaultMonitorSwitcher — Stage 3: Activity Tracker",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);

            System.Windows.Application.Current.Shutdown();
        });
    }

    private static void OnSample(object? sender, ActivitySample sample)
    {
        var line = $"[{sample.Timestamp:HH:mm:ss.ff}]  " +
                   $"Cursor={sample.CursorZone,-8}  " +
                   $"FgWindow={sample.ForegroundWindowZone,-8}  " +
                   $"Effective={sample.EffectiveZone}";
        System.Diagnostics.Debug.WriteLine(line);
        Console.WriteLine(line);
    }

    public void OnSessionEnding() { }

    public void Dispose()
    {
        _tracker?.Stop();
        _tracker?.Dispose();
    }
}
