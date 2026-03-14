using DefaultMonitorSwitcher.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DefaultMonitorSwitcher.Infrastructure.Input;

public sealed class WindowEventSource : IWindowEventSource
{
    private readonly IDisplayService _displayService;
    private readonly System.Windows.Threading.Dispatcher _uiDispatcher;

    // Strong reference prevents the delegate from being GC'd while the hook is active
    private WINEVENTPROC? _hookProc;
    private UnhookWinEventSafeHandle? _hookHandle;
    private MonitorInfo? _hdtvMonitor;

    private const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    private const uint WINEVENT_OUTOFCONTEXT    = 0x0000;
    private const int  OBJID_WINDOW             = 0;

    public WindowEventSource(
        IDisplayService displayService,
        System.Windows.Threading.Dispatcher uiDispatcher)
    {
        _displayService = displayService;
        _uiDispatcher   = uiDispatcher;
    }

    public event EventHandler? WindowMovedToHdtv;

    /// <summary>Must be called on the UI thread.</summary>
    public void Start(MonitorInfo hdtvMonitor)
    {
        if (_hookHandle != null)
            Stop();

        _hdtvMonitor = hdtvMonitor;
        _hookProc    = OnWinEvent;

        _hookHandle = PInvoke.SetWinEventHook(
            EVENT_SYSTEM_MOVESIZEEND,
            EVENT_SYSTEM_MOVESIZEEND,
            hmodWinEventProc: null,
            _hookProc,
            idProcess: 0,   // all processes
            idThread:  0,   // all threads
            WINEVENT_OUTOFCONTEXT);
    }

    public void Stop()
    {
        _hookHandle?.Dispose();
        _hookHandle = null;
        _hookProc   = null;
        _hdtvMonitor = null;
    }

    public void Dispose() => Stop();

    // ── Hook callback — fires on the UI thread ───────────────────────────────

    private unsafe void OnWinEvent(
        HWINEVENTHOOK hWinEventHook,
        uint @event,
        HWND hwnd,
        int idObject,
        int idChild,
        uint idEventThread,
        uint dwmsEventTime)
    {
        // Filter: must be the window object itself
        if (idObject != OBJID_WINDOW)
            return;

        if (hwnd == default)
            return;

        // Must be a visible top-level window
        if (!PInvoke.IsWindowVisible(hwnd))
            return;

        // Must be the root ancestor (rules out child/popup windows)
        var root = PInvoke.GetAncestor(hwnd, GET_ANCESTOR_FLAGS.GA_ROOT);
        if (root != hwnd)
            return;

        // Resolve which monitor the window's center landed on
        var monitor = _displayService.MonitorFromWindowHandle((nint)(IntPtr)hwnd);
        if (monitor == null)
            return;

        if (_hdtvMonitor != null &&
            monitor.DevicePath.Equals(_hdtvMonitor.DevicePath, StringComparison.OrdinalIgnoreCase))
        {
            WindowMovedToHdtv?.Invoke(this, EventArgs.Empty);
        }
    }
}
