using DefaultMonitorSwitcher.Infrastructure.Display;
using DefaultMonitorSwitcher.Services;

namespace DefaultMonitorSwitcher;

public sealed class AppBootstrapper : IDisposable
{
    private readonly DisplayService _displayService = new();

    public void Start()
    {
        // Stage 2 debug: enumerate monitors and print to debug output / console
        var monitors = _displayService.GetActiveMonitors();

        System.Diagnostics.Debug.WriteLine("=== Active Monitors ===");
        foreach (var m in monitors)
        {
            System.Diagnostics.Debug.WriteLine(
                $"  [{(m.IsPrimary ? "PRIMARY" : "      ")}] {m.FriendlyName}");
            System.Diagnostics.Debug.WriteLine(
                $"            Bounds:     {m.Bounds}");
            System.Diagnostics.Debug.WriteLine(
                $"            DevicePath: {m.DevicePath}");
        }

        if (monitors.Count == 0)
            System.Diagnostics.Debug.WriteLine("  (no monitors returned)");

        System.Diagnostics.Debug.WriteLine("=======================");

        // Signal app to show a message box so the user can verify output in a debugger
        // or the Output window. Then exit cleanly.
        System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Active monitors detected:\n");
            foreach (var m in monitors)
            {
                sb.AppendLine($"{(m.IsPrimary ? "[PRIMARY] " : "          ")}{m.FriendlyName}");
                sb.AppendLine($"  Bounds: {m.Bounds}");
                sb.AppendLine($"  Path:   {m.DevicePath}\n");
            }

            if (monitors.Count == 0)
                sb.AppendLine("⚠ No monitors returned — check DisplayService implementation.");

            System.Windows.MessageBox.Show(
                sb.ToString(),
                "DefaultMonitorSwitcher — Stage 2: Monitor Enumeration",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);

            System.Windows.Application.Current.Shutdown();
        });
    }

    public void OnSessionEnding() { }

    public void Dispose() { }
}
