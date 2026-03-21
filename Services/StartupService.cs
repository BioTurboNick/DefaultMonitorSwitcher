using DefaultMonitorSwitcher.Core;

namespace DefaultMonitorSwitcher.Services;

/// <summary>
/// Manages the Windows Task Scheduler logon task that starts the app on boot.
/// A scheduled task is used instead of the Run registry key so that startup
/// fires immediately at logon without the ~10-second delay Windows imposes
/// on Run key entries to improve perceived boot performance.
/// </summary>
public sealed class StartupService : IStartupService
{
    private const string TaskName = "DefaultMonitorSwitcher";

    public bool IsRegistered
    {
        get
        {
            int exit = Run("schtasks.exe", $"/Query /TN \"{TaskName}\"", SW_HIDE);
            return exit == 0;
        }
    }

    public void Register()
    {
        string exe = Environment.ProcessPath ?? "";
        Run("schtasks.exe",
            $"/Create /TN \"{TaskName}\" /TR \"\\\"{exe}\\\"\" /SC ONLOGON /F",
            SW_HIDE);
    }

    public void Unregister()
    {
        Run("schtasks.exe", $"/Delete /TN \"{TaskName}\" /F", SW_HIDE);
    }

    private const int SW_HIDE = 0;

    private static int Run(string exe, string args, int show)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(exe, args)
        {
            UseShellExecute  = false,
            CreateNoWindow   = true,
            WindowStyle      = (System.Diagnostics.ProcessWindowStyle)show,
        };
        using var p = System.Diagnostics.Process.Start(psi);
        p?.WaitForExit();
        return p?.ExitCode ?? -1;
    }
}
