using System.Runtime.InteropServices;
using DefaultMonitorSwitcher.Core;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace DefaultMonitorSwitcher.Infrastructure.Display;

public sealed class DisplayService : IDisplayService
{
    // ── Public API ──────────────────────────────────────────────────────────

    public IReadOnlyList<MonitorInfo> GetActiveMonitors()
    {
        var (paths, modes, pathCount, modeCount) = QueryDisplayConfig();
        var results = new List<MonitorInfo>();

        for (int i = 0; i < (int)pathCount; i++)
        {
            var path = paths[i];
            if (path.targetInfo.targetAvailable == 0)
                continue;

            var deviceName = GetTargetDeviceName(path);
            if (deviceName == null)
                continue;

            var bounds  = ComputeBoundsFromSourceMode(path, modes, (int)modeCount);
            var primary = IsModeAtOrigin(path, modes, (int)modeCount);

            results.Add(new MonitorInfo
            {
                DevicePath   = deviceName.Value.monitorDevicePath.ToString(),
                FriendlyName = deviceName.Value.monitorFriendlyDeviceName.ToString(),
                Bounds       = bounds,
                IsPrimary    = primary,
            });
        }

        AssignPositionLabels(results);
        return results;
    }

    public MonitorInfo? MonitorFromPoint(System.Drawing.Point point)
        => GetActiveMonitors().FirstOrDefault(m => m.Bounds.Contains(point));

    public MonitorInfo? MonitorFromWindowHandle(nint hwnd)
    {
        if (hwnd == 0)
            return null;

        // Get the HMONITOR for the window, then match its rect to our MonitorInfo list.
        var hmon = PInvoke.MonitorFromWindow((HWND)hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONULL);
        if (hmon.IsNull)
            return null;

        var rect = GetMonitorRect(hmon);
        if (rect == System.Drawing.Rectangle.Empty)
            return null;

        return GetActiveMonitors().FirstOrDefault(m => m.Bounds == rect);
    }

    public string? GetPrimaryMonitorDevicePath()
        => GetActiveMonitors().FirstOrDefault(m => m.IsPrimary)?.DevicePath;

    public bool TrySetPrimaryMonitor(string devicePath, out string? failureReason)
    {
        var (paths, modes, pathCount, modeCount) = QueryDisplayConfig();

        // Find the path whose monitorDevicePath matches
        int targetIdx = -1;
        for (int i = 0; i < (int)pathCount; i++)
        {
            var name = GetTargetDeviceName(paths[i]);
            if (name != null && name.Value.monitorDevicePath.ToString() == devicePath)
            {
                targetIdx = i;
                break;
            }
        }

        if (targetIdx < 0)
        {
            failureReason = $"Monitor '{devicePath}' not found in current topology.";
            return false;
        }

        // Find the source mode for the new primary and compute the offset to put it at (0,0)
        var newPrimary = paths[targetIdx];
        uint srcModeIdx = newPrimary.sourceInfo.Anonymous.modeInfoIdx;
        if (srcModeIdx >= modeCount ||
            modes[(int)srcModeIdx].infoType != DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
        {
            failureReason = "Could not find source mode for target monitor.";
            return false;
        }

        int dx = -modes[(int)srcModeIdx].Anonymous.sourceMode.position.x;
        int dy = -modes[(int)srcModeIdx].Anonymous.sourceMode.position.y;

        // Shift all source modes so the new primary lands at (0,0)
        for (int i = 0; i < (int)modeCount; i++)
        {
            if (modes[i].infoType == DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
            {
                modes[i].Anonymous.sourceMode.position.x += dx;
                modes[i].Anonymous.sourceMode.position.y += dy;
            }
        }

        var flags = SET_DISPLAY_CONFIG_FLAGS.SDC_USE_SUPPLIED_DISPLAY_CONFIG
                  | SET_DISPLAY_CONFIG_FLAGS.SDC_APPLY
                  | SET_DISPLAY_CONFIG_FLAGS.SDC_SAVE_TO_DATABASE
                  | SET_DISPLAY_CONFIG_FLAGS.SDC_ALLOW_CHANGES;

        int result = PInvoke.SetDisplayConfig(
            paths.AsSpan(0, (int)pathCount),
            modes.AsSpan(0, (int)modeCount),
            flags);

        if (result != 0)
        {
            failureReason = $"SetDisplayConfig failed with error {result}.";
            return false;
        }

        failureReason = null;
        return true;
    }

    // ── Position labels ──────────────────────────────────────────────────────

    private static void AssignPositionLabels(List<MonitorInfo> monitors)
    {
        var sorted = monitors.OrderBy(m => m.Bounds.X).ToList();
        string[] names = sorted.Count switch
        {
            1 => [""],
            2 => ["Left", "Right"],
            3 => ["Left", "Center", "Right"],
            _ => sorted.Select((_, i) => $"#{i + 1}").ToArray(),
        };

        for (int i = 0; i < sorted.Count; i++)
        {
            var m = sorted[i];
            var label = names[i].Length > 0 ? $"{names[i]} \u2014 {m.FriendlyName}" : m.FriendlyName;
            var idx = monitors.IndexOf(m);
            monitors[idx] = m with { DisplayLabel = label };
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static (DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes,
                    uint pathCount, uint modeCount) QueryDisplayConfig()
    {
        WIN32_ERROR result;

        result = PInvoke.GetDisplayConfigBufferSizes(
            QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS,
            out uint pathCount,
            out uint modeCount);

        if (result != WIN32_ERROR.ERROR_SUCCESS)
            return ([], [], 0, 0);

        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

        result = PInvoke.QueryDisplayConfig(
            QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS,
            ref pathCount,
            paths.AsSpan(),
            ref modeCount,
            modes.AsSpan());

        if (result != WIN32_ERROR.ERROR_SUCCESS)
            return ([], [], 0, 0);

        return (paths, modes, pathCount, modeCount);
    }

    private static DISPLAYCONFIG_TARGET_DEVICE_NAME? GetTargetDeviceName(DISPLAYCONFIG_PATH_INFO path)
    {
        var req = new DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type      = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                size      = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                adapterId = path.targetInfo.adapterId,
                id        = path.targetInfo.id,
            }
        };

        int result = PInvoke.DisplayConfigGetDeviceInfo(ref req.header);
        return result == 0 ? req : null;
    }

    private static System.Drawing.Rectangle ComputeBoundsFromSourceMode(
        DISPLAYCONFIG_PATH_INFO path,
        DISPLAYCONFIG_MODE_INFO[] modes,
        int modeCount)
    {
        uint idx = path.sourceInfo.Anonymous.modeInfoIdx;
        if (idx >= modeCount)
            return System.Drawing.Rectangle.Empty;

        var mode = modes[(int)idx];
        if (mode.infoType != DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
            return System.Drawing.Rectangle.Empty;

        var sm = mode.Anonymous.sourceMode;
        return new System.Drawing.Rectangle(
            sm.position.x,
            sm.position.y,
            (int)sm.width,
            (int)sm.height);
    }

    private static bool IsModeAtOrigin(
        DISPLAYCONFIG_PATH_INFO path,
        DISPLAYCONFIG_MODE_INFO[] modes,
        int modeCount)
    {
        uint idx = path.sourceInfo.Anonymous.modeInfoIdx;
        if (idx >= modeCount)
            return false;

        var mode = modes[(int)idx];
        if (mode.infoType != DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
            return false;

        var pos = mode.Anonymous.sourceMode.position;
        return pos.x == 0 && pos.y == 0;
    }

    private static System.Drawing.Rectangle GetMonitorRect(HMONITOR hmon)
    {
        unsafe
        {
            var infoEx = new MONITORINFOEXW();
            infoEx.monitorInfo.cbSize = (uint)Marshal.SizeOf<MONITORINFOEXW>();

            if (!PInvoke.GetMonitorInfo(hmon, ref infoEx.monitorInfo))
                return System.Drawing.Rectangle.Empty;

            var r = infoEx.monitorInfo.rcMonitor;
            return System.Drawing.Rectangle.FromLTRB(r.left, r.top, r.right, r.bottom);
        }
    }
}
