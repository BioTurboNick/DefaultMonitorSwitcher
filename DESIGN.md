# DefaultMonitorSwitcher — Architecture Design

## Minimum Requirements

- **OS**: Windows 10 version 1803 (April 2018 Update) or later
  — required for `IAudioPolicyConfig`
- **Runtime**: .NET 10

## 1. Folder / Namespace Structure

```
DefaultMonitorSwitcher/
├── DefaultMonitorSwitcher.csproj
├── App.xaml / App.xaml.cs                    — DefaultMonitorSwitcher
├── AppBootstrapper.cs                        — DefaultMonitorSwitcher
│
├── Core/
│   ├── Enums.cs                              — DefaultMonitorSwitcher.Core
│   ├── Records.cs                            — DefaultMonitorSwitcher.Core
│   ├── IActivityTracker.cs                   — DefaultMonitorSwitcher.Core
│   ├── ISwitchController.cs                  — DefaultMonitorSwitcher.Core
│   ├── IDisplayService.cs                    — DefaultMonitorSwitcher.Core
│   ├── IAudioService.cs                      — DefaultMonitorSwitcher.Core
│   ├── IConfigurationService.cs              — DefaultMonitorSwitcher.Core
│   ├── INotificationService.cs               — DefaultMonitorSwitcher.Core
│   ├── IStartupService.cs                    — DefaultMonitorSwitcher.Core
│   └── IWindowEventSource.cs                 — DefaultMonitorSwitcher.Core
│
├── Services/
│   ├── ActivityTracker.cs                    — DefaultMonitorSwitcher.Services
│   ├── SwitchController.cs                   — DefaultMonitorSwitcher.Services
│   ├── ConfigurationService.cs               — DefaultMonitorSwitcher.Services
│   ├── NotificationService.cs                — DefaultMonitorSwitcher.Services
│   └── StartupService.cs                     — DefaultMonitorSwitcher.Services
│
├── Infrastructure/
│   ├── Display/
│   │   └── DisplayService.cs                 — DefaultMonitorSwitcher.Infrastructure.Display
│   ├── Audio/
│   │   ├── AudioService.cs                   — DefaultMonitorSwitcher.Infrastructure.Audio
│   │   ├── PolicyConfigInterop.cs            — DefaultMonitorSwitcher.Infrastructure.Audio
│   │   └── MmDeviceInterop.cs                — DefaultMonitorSwitcher.Infrastructure.Audio
│   └── Input/
│       └── WindowEventSource.cs              — DefaultMonitorSwitcher.Infrastructure.Input
│
├── NativeMethods.txt                         — CsWin32 source generator input
│
└── UI/
    ├── TrayIconViewModel.cs                  — DefaultMonitorSwitcher.UI
    ├── TrayIconView.xaml / .cs               — DefaultMonitorSwitcher.UI
    ├── Settings/
    │   ├── SettingsViewModel.cs              — DefaultMonitorSwitcher.UI.Settings
    │   └── SettingsWindow.xaml / .cs         — DefaultMonitorSwitcher.UI.Settings
    └── Resources/
        ├── Icons/                            — (embedded .ico/.png assets)
        └── Converters.cs                     — DefaultMonitorSwitcher.UI
```

---

## 2. Enums and Records

### `DefaultMonitorSwitcher.Core` — `Enums.cs`

```csharp
namespace DefaultMonitorSwitcher.Core;

/// <summary>The zone where activity was last attributed.</summary>
public enum ActivityZone { None, Desktop, Hdtv }

/// <summary>The high-level state of the switch state machine.</summary>
public enum SwitcherState
{
    /// <summary>Desktop monitors are primary; no HDTV dwell accumulating.</summary>
    DesktopIdle,

    /// <summary>Desktop is primary; HDTV dwell counter accumulating toward forward switch.</summary>
    DesktopHdtvDwelling,

    /// <summary>HDTV is primary; no revert condition accumulating.</summary>
    HdtvActive,

    /// <summary>HDTV is primary; idle countdown running (no HDTV activity).</summary>
    HdtvIdleCountdown,

    /// <summary>HDTV is primary; desktop dwell counter accumulating toward early revert.</summary>
    HdtvDesktopDwelling,
}

/// <summary>The reason a switch (revert or forward) was requested.</summary>
public enum SwitchReason
{
    Startup,
    IdleTimeout,
    DesktopDwell,
    SessionEnding,
    HdtvDwell,
    Manual,
}

/// <summary>Direction of the switch.</summary>
public enum SwitchDirection { ToDesktop, ToHdtv }

/// <summary>Tray icon visual state.</summary>
public enum TrayIconState { Neutral, Active, IdleCountdown, TvShowMode }
```

### `DefaultMonitorSwitcher.Core` — `Records.cs`

```csharp
namespace DefaultMonitorSwitcher.Core;

/// <summary>
/// Immutable snapshot of a connected display's identity.
/// FriendlyName comes from QueryDisplayConfig DISPLAYCONFIG_TARGET_DEVICE_NAME.
/// DevicePath is the stable \\?\DISPLAY#... path, not the volatile \\.\DISPLAYn number.
/// </summary>
public sealed record MonitorInfo(
    string DevicePath,       // stable \\?\DISPLAY#... form from DISPLAYCONFIG_TARGET_DEVICE_NAME
    string FriendlyName,     // EDID model name, e.g. "QBQ90" or "Dell S2721QS"
    bool   IsPrimary,
    ulong  AdapterId,        // LUID as ulong (low | high << 32)
    uint   TargetId
);

/// <summary>
/// Immutable snapshot of an audio playback endpoint.
/// </summary>
public sealed record AudioEndpointInfo(
    string DeviceId,         // IMMDevice.GetId()
    string FriendlyName      // PKEY_Device_FriendlyName, e.g. "QBQ90 (NVIDIA High Definition Audio)"
);

/// <summary>
/// Result of an attempted switch operation.
/// </summary>
public sealed record SwitchResult(
    bool   DisplaySwitched,
    bool   AudioSwitched,
    string? AudioFailureReason
);

/// <summary>
/// Carries the result of one activity poll tick.
/// </summary>
public sealed record ActivitySample(
    ActivityZone   CursorZone,
    ActivityZone   ForegroundWindowZone,
    /// <summary>Effective zone after precedence and mouse-dwell smoothing rules.</summary>
    ActivityZone   EffectiveZone,
    DateTimeOffset Timestamp
);

/// <summary>
/// All persisted user configuration. Serialised to / from config.json.
/// </summary>
public sealed record AppConfiguration
{
    public string? HdtvDisplayDevicePath            { get; init; }
    public string? PreferredPrimaryDisplayDevicePath { get; init; }
    /// <summary>null = auto-detected from EDID name match.</summary>
    public string? HdtvAudioDeviceId                { get; init; }
    /// <summary>null = auto-detected from preferred primary monitor's EDID name.</summary>
    public string? DesktopAudioDeviceId             { get; init; }
    public bool    AudioSwitchingEnabled            { get; init; } = true;
    public bool    RespectManualAudioOverride       { get; init; } = false;
    public int     IdleTimeoutSeconds               { get; init; } = 300;
    public int     DesktopDwellSeconds              { get; init; } = 120;
    public int     HdtvDwellSeconds                 { get; init; } = 60;
    public int     MouseDwellSeconds                { get; init; } = 10;
    public int     PollIntervalSeconds              { get; init; } = 5;
    public int     ElevatedPollIntervalSeconds      { get; init; } = 1;
    public int     ElevatedPollDurationSeconds      { get; init; } = 30;
    public bool    TvShowModeEnabled                { get; init; } = false;
    public bool    RunOnStartup                     { get; init; } = true;
}
```

---

## 3. Core Interfaces

### `IConfigurationService.cs`

```csharp
namespace DefaultMonitorSwitcher.Core;

public interface IConfigurationService
{
    /// <summary>Currently loaded configuration. Always non-null after InitializeAsync().</summary>
    AppConfiguration Current { get; }

    /// <summary>Raised on the UI thread after Current changes.</summary>
    event EventHandler<AppConfiguration> ConfigurationChanged;

    ValueTask InitializeAsync(CancellationToken ct = default);

    /// <summary>Atomically replaces Current and persists to disk (synchronous JSON write).</summary>
    void Save(AppConfiguration configuration);
}
```

### `IDisplayService.cs`

```csharp
namespace DefaultMonitorSwitcher.Core;

public interface IDisplayService
{
    /// <summary>Enumerates currently active monitors via QueryDisplayConfig.</summary>
    IReadOnlyList<MonitorInfo> GetActiveMonitors();

    /// <summary>Returns the monitor containing the given screen-coordinate point, or null.</summary>
    MonitorInfo? MonitorFromPoint(int x, int y);

    /// <summary>Returns the monitor containing the foreground window's largest overlap, or null.</summary>
    MonitorInfo? GetForegroundWindowMonitor();

    /// <summary>
    /// Applies a new primary display via SetDisplayConfig (SDC_APPLY | SDC_SAVE_TO_DATABASE).
    /// Throws DisplaySwitchException on failure.
    /// </summary>
    void SetPrimaryMonitor(MonitorInfo monitor);

    /// <summary>Returns true if the given monitor is currently the Windows primary display.</summary>
    bool IsPrimary(MonitorInfo monitor);
}
```

### `IAudioService.cs`

```csharp
namespace DefaultMonitorSwitcher.Core;

public interface IAudioService
{
    /// <summary>Enumerates active audio playback endpoints via IMMDeviceEnumerator.</summary>
    IReadOnlyList<AudioEndpointInfo> GetPlaybackEndpoints();

    /// <summary>Returns the current Windows default playback device ID, or null.</summary>
    string? GetDefaultPlaybackDeviceId();

    /// <summary>
    /// Case-insensitive substring match of monitor.FriendlyName in endpoint.FriendlyName.
    /// Returns the best match or null.
    /// </summary>
    AudioEndpointInfo? AutoDetectEndpointForMonitor(MonitorInfo monitor);

    /// <summary>
    /// Sets the Windows default playback device for all three ERole values via IAudioPolicyConfig.
    /// Returns false (does not throw) on COM failure; populates failureReason.
    /// </summary>
    bool TrySetDefaultPlaybackDevice(string deviceId, out string? failureReason);
}
```

### `IActivityTracker.cs`

```csharp
namespace DefaultMonitorSwitcher.Core;

public interface IActivityTracker : IDisposable
{
    /// <summary>Raised on the background poll thread each time a tick completes.</summary>
    event EventHandler<ActivitySample> SampleProduced;

    /// <summary>
    /// Temporarily reduces the poll interval to ElevatedPollIntervalSeconds for
    /// ElevatedPollDurationSeconds. Calling again while already elevated resets the expiry.
    /// </summary>
    void ActivateElevatedPolling();

    void Start();
    void Stop();
}
```

### `IWindowEventSource.cs`

```csharp
namespace DefaultMonitorSwitcher.Core;

public interface IWindowEventSource : IDisposable
{
    /// <summary>
    /// Fired on the UI thread when a top-level window finishes moving and resolves
    /// to the HDTV monitor. Not fired for moves to any other monitor.
    /// </summary>
    event EventHandler WindowMovedToHdtv;

    /// <summary>Must be called on the UI thread (WinEventHook requires a message loop).</summary>
    void Start(MonitorInfo hdtvMonitor);
    void Stop();
}
```

### `ISwitchController.cs`

```csharp
namespace DefaultMonitorSwitcher.Core;

public interface ISwitchController : IDisposable
{
    SwitcherState CurrentState { get; }
    bool TvShowModeEnabled { get; set; }

    /// <summary>Raised on the UI thread when CurrentState changes.</summary>
    event EventHandler<SwitcherState> StateChanged;

    /// <summary>Raised on the UI thread after any switch completes.</summary>
    event EventHandler<(SwitchDirection Direction, SwitchReason Reason, SwitchResult Result)> SwitchCompleted;

    /// <summary>
    /// Subscribes to activity and window-move events and begins monitoring.
    /// Performs a startup revert if the HDTV is already primary.
    /// </summary>
    ValueTask StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Immediate synchronous revert. Safe to call from WM_ENDSESSION.
    /// No-op if a desktop monitor is already primary.
    /// </summary>
    SwitchResult RevertNow(SwitchReason reason);

    /// <summary>Immediate synchronous forward switch. No-op if HDTV is already primary.</summary>
    SwitchResult ForwardSwitchNow(SwitchReason reason);
}
```

### `INotificationService.cs`

```csharp
namespace DefaultMonitorSwitcher.Core;

public interface INotificationService
{
    void ShowSwitchNotification(SwitchDirection direction, SwitchReason reason, SwitchResult result);
    void ShowWarning(string message);
}
```

### `IStartupService.cs`

```csharp
namespace DefaultMonitorSwitcher.Core;

public interface IStartupService
{
    bool IsRegistered { get; }
    void Register();
    void Unregister();
}
```

---

## 4. Service Implementations

### `Services/ActivityTracker.cs`

```csharp
namespace DefaultMonitorSwitcher.Services;

public sealed class ActivityTracker : IActivityTracker
{
    public ActivityTracker(
        IDisplayService displayService,
        IConfigurationService configService);

    public event EventHandler<ActivitySample>? SampleProduced;
    public void ActivateElevatedPolling();   // sets _elevatedExpiry = now + ElevatedPollDurationSeconds
    public void Start();
    public void Stop();
    public void Dispose();

    // Internals
    private Task _loopTask;
    private CancellationTokenSource _cts;
    private DateTimeOffset? _elevatedExpiry;           // null = not elevated
    private readonly Lock _elevatedLock = new();
    private readonly Dictionary<string, DateTimeOffset> _cursorZoneEntry = new(); // DevicePath → entry time
    private async Task PollLoopAsync(CancellationToken ct);
    private ActivityZone GetCursorZone(IReadOnlyList<MonitorInfo> monitors);      // applies mouse-dwell smoothing
    private ActivityZone GetForegroundWindowZone(IReadOnlyList<MonitorInfo> monitors);
    private static ActivityZone Classify(MonitorInfo? monitor, AppConfiguration cfg);
}
```

**Elevated polling implementation note**: The loop uses `PeriodicTimer` created at `Start()` with the normal interval. When `ActivateElevatedPolling` is called, it sets `_elevatedExpiry` and cancels + restarts the loop with the elevated interval. When the loop tick detects `_elevatedExpiry` has passed, it cancels + restarts with the normal interval. Restart is safe because `_loopTask` is awaited inside `Stop()`/`Dispose()`.

### `Services/SwitchController.cs`

```csharp
namespace DefaultMonitorSwitcher.Services;

public sealed class SwitchController : ISwitchController
{
    public SwitchController(
        IActivityTracker activityTracker,
        IWindowEventSource windowEventSource,
        IDisplayService displayService,
        IAudioService audioService,
        IConfigurationService configService,
        INotificationService notificationService,
        System.Windows.Threading.Dispatcher uiDispatcher);

    public SwitcherState CurrentState { get; private set; }
    public bool TvShowModeEnabled { get; set; }
    public event EventHandler<SwitcherState>? StateChanged;
    public event EventHandler<(SwitchDirection, SwitchReason, SwitchResult)>? SwitchCompleted;
    public ValueTask StartAsync(CancellationToken ct = default);
    public SwitchResult RevertNow(SwitchReason reason);
    public SwitchResult ForwardSwitchNow(SwitchReason reason);
    public void Dispose();

    // State machine internals
    private readonly Lock _stateLock = new();
    private DateTimeOffset _hdtvLastActivityTime;      // used for HdtvIdleCountdown
    private DateTimeOffset _desktopDwellStartTime;     // used for HdtvDesktopDwelling
    private DateTimeOffset _hdtvDwellStartTime;        // used for DesktopHdtvDwelling
    private void OnSampleProduced(object? sender, ActivitySample sample);   // called on background thread
    private void OnWindowMovedToHdtv(object? sender, EventArgs e);          // called on UI thread
    private void TransitionTo(SwitcherState newState);                      // must be called under _stateLock
    private SwitchResult ExecuteSwitch(SwitchDirection direction, SwitchReason reason);
    private MonitorInfo? ResolveTargetDesktopMonitor();   // preferred → fallback → null
    private MonitorInfo? ResolveHdtvMonitor();
    private string? ResolveAudioDeviceId(SwitchDirection direction);
    // ResolveAudioDeviceId returns null (skip audio switch) when:
    //   - AudioSwitchingEnabled == false, OR
    //   - RespectManualAudioOverride == true AND GetDefaultPlaybackDeviceId()
    //     does not match the expected source device for this direction
    //     (i.e. the user has manually changed audio away from the expected device)
    // Otherwise returns the target device ID (config override if set, else auto-detected).
}
```

**State transition table:**

| From state | Condition | To state |
|---|---|---|
| `DesktopIdle` | Sample: EffectiveZone == Hdtv | `DesktopHdtvDwelling` |
| `DesktopHdtvDwelling` | Sample: EffectiveZone != Hdtv | `DesktopIdle` |
| `DesktopHdtvDwelling` | now − _hdtvDwellStartTime ≥ HdtvDwellSeconds | Forward switch → `HdtvActive` |
| `HdtvActive` | Sample: EffectiveZone == None | `HdtvIdleCountdown` |
| `HdtvActive` | Sample: EffectiveZone == Desktop | `HdtvDesktopDwelling` |
| `HdtvIdleCountdown` | Sample: EffectiveZone == Hdtv | `HdtvActive` |
| `HdtvIdleCountdown` | now − _hdtvLastActivityTime ≥ IdleTimeoutSeconds | Revert → `DesktopIdle` |
| `HdtvDesktopDwelling` | Sample: EffectiveZone == Hdtv | `HdtvActive` |
| `HdtvDesktopDwelling` | EffectiveZone == Desktop && elapsed ≥ DesktopDwellSeconds && !TvShowMode | Revert → `DesktopIdle` |

### `Services/ConfigurationService.cs`

```csharp
namespace DefaultMonitorSwitcher.Services;

public sealed class ConfigurationService : IConfigurationService
{
    public ConfigurationService();  // no injected deps

    public AppConfiguration Current { get; private set; } = new();
    public event EventHandler<AppConfiguration>? ConfigurationChanged;
    public ValueTask InitializeAsync(CancellationToken ct = default);
    public void Save(AppConfiguration configuration);

    // %LOCALAPPDATA%\DefaultMonitorSwitcher\config.json
    private static string ConfigFilePath { get; }
}
```

### `Services/NotificationService.cs`

```csharp
namespace DefaultMonitorSwitcher.Services;

public sealed class NotificationService : INotificationService
{
    public NotificationService();

    public void ShowSwitchNotification(SwitchDirection direction, SwitchReason reason, SwitchResult result);
    public void ShowWarning(string message);

    private void Show(string title, string body);

    private const string AppId    = "DefaultMonitorSwitcher";
    private const string ToastTag = "switch";
    private const string ToastGroup = "main";
    // ExpirationTime = DateTimeOffset.Now + TimeSpan.FromMinutes(3)
    // Tag = ToastTag ensures replacement; only one entry ever in Action Center
}
```

### `Services/StartupService.cs`

```csharp
namespace DefaultMonitorSwitcher.Services;

public sealed class StartupService : IStartupService
{
    public StartupService();  // reads Environment.ProcessPath

    public bool IsRegistered { get; }
    public void Register();
    public void Unregister();

    // HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run
    // Value name: "DefaultMonitorSwitcher"
}
```

---

## 5. Infrastructure Layer

### `NativeMethods.txt` — CsWin32 input

All documented Win32 P/Invoke declarations and struct definitions are generated
by the **CsWin32** source generator (`Microsoft.Windows.CsWin32`) from the
official Windows SDK metadata. This eliminates all manual `[StructLayout]`,
`[FieldOffset]`, and `[LibraryImport]` declarations for documented APIs —
including the complex union-containing display config structs.

```
# Display config
QueryDisplayConfig
SetDisplayConfig
DisplayConfigGetDeviceInfo
DISPLAYCONFIG_PATH_INFO
DISPLAYCONFIG_MODE_INFO
DISPLAYCONFIG_TARGET_DEVICE_NAME
DISPLAYCONFIG_MODE_INFO_TYPE

# Monitor helpers
GetMonitorInfo
MONITORINFOEX

# Cursor / window
GetCursorPos
GetForegroundWindow
MonitorFromPoint
MonitorFromWindow

# Window event hook
SetWinEventHook
UnhookWinEvent
WINEVENTPROC

# Window helpers
GetAncestor
IsWindowVisible
GetWindowThreadProcessId
```

CsWin32 emits these into the `Windows.Win32` namespace hierarchy (e.g.
`Windows.Win32.Graphics.Gdi`, `Windows.Win32.UI.WindowsAndMessaging`).
`DisplayService` and `WindowEventSource` use the generated types directly —
no wrapper file needed.

> **IMMDeviceEnumerator coverage**: CsWin32 also covers `IMMDeviceEnumerator`,
> `IMMDevice`, `IMMDeviceCollection`, `IPropertyStore`, and `PROPERTYKEY` via
> the `Windows.Win32.Media.Audio` namespace. `MmDeviceInterop.cs` should be
> removed in favour of these generated types once verified at implementation time.
> `IAudioPolicyConfig` (undocumented) cannot be generated and remains manual.

### `Infrastructure/Display/DisplayService.cs`

```csharp
namespace DefaultMonitorSwitcher.Infrastructure.Display;

public sealed class DisplayService : IDisplayService
{
    public DisplayService();

    public IReadOnlyList<MonitorInfo> GetActiveMonitors();
    public MonitorInfo? MonitorFromPoint(int x, int y);
    public MonitorInfo? GetForegroundWindowMonitor();
    public void SetPrimaryMonitor(MonitorInfo monitor);
    public bool IsPrimary(MonitorInfo monitor);

    // SetPrimaryMonitor implementation:
    //   1. QueryDisplayConfig → paths[], modes[]
    //   2. Find the DISPLAYCONFIG_MODE_INFO (type == DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
    //      whose targetId matches monitor.TargetId and adapterId matches monitor.AdapterId
    //   3. Compute offset = -sourceMode.position so the target lands at (0,0)
    //   4. Apply offset to all source positions in modes[]
    //   5. SetDisplayConfig(SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG |
    //                       SDC_SAVE_TO_DATABASE | SDC_ALLOW_CHANGES)
    private static MonitorInfo BuildMonitorInfo(
        in DISPLAYCONFIG_PATH_INFO path,
        DISPLAYCONFIG_MODE_INFO[] modes);
}

public sealed class DisplaySwitchException(string message, int win32Error)
    : Exception(message);
```

### `Infrastructure/Audio/PolicyConfigInterop.cs`

```csharp
namespace DefaultMonitorSwitcher.Infrastructure.Audio;

// IAudioPolicyConfig — undocumented Windows internal interface available on
// Windows 10 1803+. Preferred over the older IPolicyConfig because its vtable
// is shorter and better verified by open-source projects (notably EarTrumpet).
// IID sourced from EarTrumpet and cross-verified against Windows.Media.Audio.dll symbols.
// Vtable must be verified against EarTrumpet's implementation before shipping:
//   https://github.com/File-New-Project/EarTrumpet
[ComImport]
[Guid("2a59116d-6c4f-45e0-a74f-707e3fef9258")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioPolicyConfig
{
    // Slots 0–2: IUnknown (handled by runtime)
    // Each stub below holds a vtable slot. Only SetDefaultEndpoint is called;
    // all others are declared solely to keep slot indices correct.
    void NotImpl1();  // slot 3
    void NotImpl2();  // slot 4
    void NotImpl3();  // slot 5
    void NotImpl4();  // slot 6
    void NotImpl5();  // slot 7
    void NotImpl6();  // slot 8
    void NotImpl7();  // slot 9
    void NotImpl8();  // slot 10

    /// <summary>
    /// Sets the Windows default audio playback endpoint for the given role.
    /// Call for eConsole, eMultimedia, and eCommunications to fully switch.
    /// </summary>
    [PreserveSig]
    int SetDefaultEndpoint(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
        ERole role);                                               // slot 11
}

// CLSID of the concrete COM class implementing IAudioPolicyConfig
[ComImport]
[Guid("1776DCD9-FA97-4463-A8B3-AD4B5C5DAD27")]
internal class AudioPolicyConfigFactory { }

internal enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }
```

> **Implementation note**: Before writing `AudioService`, verify the exact slot count
> and `SetDefaultEndpoint` index against EarTrumpet's current `AudioPolicyConfig.cs`.
> The `NotImpl` stub count above is a placeholder pending that verification.
> Correct vtable verification is the single highest-risk step in the audio subsystem.

### `Infrastructure/Audio/MmDeviceInterop.cs`

```csharp
namespace DefaultMonitorSwitcher.Infrastructure.Audio;

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    void EnumAudioEndpoints(
        EDataFlow dataFlow,
        uint dwStateMask,
        out IMMDeviceCollection ppDevices);

    void GetDefaultAudioEndpoint(
        EDataFlow dataFlow,
        ERole role,
        out IMMDevice ppEndpoint);

    void GetDevice(
        [MarshalAs(UnmanagedType.LPWStr)] string pwstrId,
        out IMMDevice ppDevice);

    void RegisterEndpointNotificationCallback(nint pClient);
    void UnregisterEndpointNotificationCallback(nint pClient);
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorClass { }

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    void GetCount(out uint pcDevices);
    void Item(uint nDevice, out IMMDevice ppDevice);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    void Activate(in Guid iid, uint dwClsCtx, nint pActivationParams, out nint ppInterface);
    void OpenPropertyStore(uint stgmAccess, out IPropertyStore ppProperties);
    void GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
    void GetState(out uint pdwState);
}

[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    void GetCount(out uint cProps);
    void GetAt(uint iProp, out PROPERTYKEY pkey);
    void GetValue(in PROPERTYKEY key, out PROPVARIANT pv);
    void SetValue(in PROPERTYKEY key, in PROPVARIANT pv);
    void Commit();
}

[StructLayout(LayoutKind.Sequential)]
internal struct PROPERTYKEY
{
    public Guid fmtid;
    public uint pid;

    // {a45c254e-df1c-4efd-8020-67d146a850e0}, 14
    internal static readonly PROPERTYKEY FriendlyName =
        new() { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
}

[StructLayout(LayoutKind.Explicit)]
internal struct PROPVARIANT
{
    [FieldOffset(0)] public ushort vt;          // VT_LPWSTR = 31
    [FieldOffset(8)] public nint   pwszVal;     // the string pointer when vt == VT_LPWSTR
}

internal enum EDataFlow { eRender = 0, eCapture = 1, eAll = 2 }

internal const uint DEVICE_STATE_ACTIVE = 0x00000001;
internal const uint STGM_READ           = 0x00000000;
```

### `Infrastructure/Audio/AudioService.cs`

```csharp
namespace DefaultMonitorSwitcher.Infrastructure.Audio;

public sealed class AudioService : IAudioService
{
    public AudioService();

    public IReadOnlyList<AudioEndpointInfo> GetPlaybackEndpoints();
    public string? GetDefaultPlaybackDeviceId();
    public AudioEndpointInfo? AutoDetectEndpointForMonitor(MonitorInfo monitor);
    public bool TrySetDefaultPlaybackDevice(string deviceId, out string? failureReason);

    // AutoDetectEndpointForMonitor:
    //   StringComparison.OrdinalIgnoreCase Contains check of monitor.FriendlyName
    //   in each endpoint.FriendlyName. Returns first match.

    // TrySetDefaultPlaybackDevice:
    //   Creates AudioPolicyConfigFactory COM object, casts to IAudioPolicyConfig.
    //   Calls SetDefaultEndpoint(deviceId, eConsole),
    //         SetDefaultEndpoint(deviceId, eMultimedia),
    //         SetDefaultEndpoint(deviceId, eCommunications).
    //   Returns false + failureReason on any COMException.
}
```

### `Infrastructure/Input/WindowEventSource.cs`

```csharp
namespace DefaultMonitorSwitcher.Infrastructure.Input;

public sealed class WindowEventSource : IWindowEventSource
{
    public WindowEventSource(
        IDisplayService displayService,
        System.Windows.Threading.Dispatcher uiDispatcher);

    public event EventHandler? WindowMovedToHdtv;

    /// <summary>Must be called on the UI thread.</summary>
    public void Start(MonitorInfo hdtvMonitor);
    public void Stop();
    public void Dispose();

    // SetWinEventHook called on the UI thread — WPF's Dispatcher is the message loop.
    // _hookProc fires on the UI thread as part of normal WPF message dispatch.
    // Inside _hookProc:
    //   - Filters to idObject == OBJID_WINDOW and hwnd != IntPtr.Zero
    //   - Checks IsWindowVisible(hwnd) && GetAncestor(hwnd, GA_ROOT) == hwnd
    //     to restrict to top-level application windows
    //   - Calls IDisplayService.MonitorFromPoint using window center
    //   - If monitor matches _hdtvMonitor.DevicePath: raises WindowMovedToHdtv
    // Holds a strong GC reference to the delegate to prevent GC collection.
    // Type is Windows.Win32.UI.Accessibility.WINEVENTPROC (generated by CsWin32).
    private WINEVENTPROC _hookProc = null!;
    private HWINEVENTHOOK _hookHandle;
    private MonitorInfo? _hdtvMonitor;
}
```

---

## 6. UI Layer

### `UI/TrayIconViewModel.cs`

```csharp
namespace DefaultMonitorSwitcher.UI;

public sealed partial class TrayIconViewModel : ObservableObject, IDisposable
{
    public TrayIconViewModel(
        ISwitchController switchController,
        IConfigurationService configService);

    [ObservableProperty] private TrayIconState _iconState = TrayIconState.Neutral;
    [ObservableProperty] private string _statusText = "Desktop primary";
    [ObservableProperty] private bool _tvShowModeEnabled;
    [ObservableProperty] private bool _isHdtvPrimary;

    [RelayCommand] private void RevertNow();
    [RelayCommand] private void ToggleTvShowMode();
    [RelayCommand] private void OpenSettings();
    [RelayCommand] private void Exit();

    partial void OnTvShowModeEnabledChanged(bool value);  // propagates to ISwitchController

    public void Dispose();
}
```

### `UI/Settings/SettingsViewModel.cs`

```csharp
namespace DefaultMonitorSwitcher.UI.Settings;

public sealed partial class SettingsViewModel : ObservableObject
{
    public SettingsViewModel(
        IConfigurationService configService,
        IDisplayService displayService,
        IAudioService audioService,
        IStartupService startupService);

    // Display
    public ObservableCollection<MonitorInfo> AvailableMonitors { get; } = new();
    [ObservableProperty] private MonitorInfo? _selectedHdtvMonitor;
    [ObservableProperty] private MonitorInfo? _selectedDesktopPrimaryMonitor;

    // Audio
    public ObservableCollection<AudioEndpointInfo> AvailableEndpoints { get; } = new();
    [ObservableProperty] private AudioEndpointInfo? _selectedHdtvAudioEndpoint;
    [ObservableProperty] private AudioEndpointInfo? _selectedDesktopAudioEndpoint;
    [ObservableProperty] private string? _autoDetectedHdtvAudioName;
    [ObservableProperty] private string? _autoDetectedDesktopAudioName;
    [ObservableProperty] private bool _audioSwitchingEnabled;
    [ObservableProperty] private bool _respectManualAudioOverride;

    // Thresholds
    [ObservableProperty] private int _idleTimeoutSeconds;
    [ObservableProperty] private int _desktopDwellSeconds;
    [ObservableProperty] private int _hdtvDwellSeconds;
    [ObservableProperty] private int _mouseDwellSeconds;
    [ObservableProperty] private int _pollIntervalSeconds;
    [ObservableProperty] private int _elevatedPollIntervalSeconds;
    [ObservableProperty] private int _elevatedPollDurationSeconds;

    // Misc
    [ObservableProperty] private bool _tvShowModeEnabled;
    [ObservableProperty] private bool _runOnStartup;

    [RelayCommand] private void Save();
    [RelayCommand] private void Cancel();
    [RelayCommand] private void RefreshDevices();

    // Partial callbacks
    partial void OnSelectedHdtvMonitorChanged(MonitorInfo? value);          // refreshes auto-detect labels
    partial void OnSelectedDesktopPrimaryMonitorChanged(MonitorInfo? value); // refreshes auto-detect labels
}
```

### `UI/Converters.cs`

```csharp
namespace DefaultMonitorSwitcher.UI;

/// <summary>Maps TrayIconState → ImageSource (one of four embedded .ico assets).</summary>
public sealed class TrayIconStateToImageSourceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Maps SwitcherState → human-readable status string for the tray tooltip/menu.</summary>
public sealed class SwitcherStateToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

---

## 7. Bootstrap and Entry Point

### `AppBootstrapper.cs`

```csharp
namespace DefaultMonitorSwitcher;

internal static class AppBootstrapper
{
    internal static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        // Infrastructure
        services.AddSingleton<IDisplayService, DisplayService>();
        services.AddSingleton<IAudioService,   AudioService>();
        services.AddSingleton<IWindowEventSource, WindowEventSource>();

        // Services
        services.AddSingleton<IConfigurationService, ConfigurationService>();
        services.AddSingleton<IActivityTracker,      ActivityTracker>();
        services.AddSingleton<ISwitchController,     SwitchController>();
        services.AddSingleton<INotificationService,  NotificationService>();
        services.AddSingleton<IStartupService,       StartupService>();

        // UI
        services.AddSingleton<TrayIconViewModel>();
        services.AddTransient<SettingsViewModel>();  // fresh snapshot on each Settings open

        // WPF Dispatcher — allows infrastructure/services to marshal to UI thread
        // without a direct dependency on System.Windows.Application
        services.AddSingleton(_ => System.Windows.Application.Current.Dispatcher);

        return services.BuildServiceProvider();
    }
}
```

### `App.xaml.cs`

```csharp
namespace DefaultMonitorSwitcher;

public partial class App : Application
{
    private ServiceProvider _services = null!;
    private ISwitchController _controller = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _services = AppBootstrapper.BuildServiceProvider();

        var config = _services.GetRequiredService<IConfigurationService>();
        await config.InitializeAsync();

        var startup = _services.GetRequiredService<IStartupService>();
        if (config.Current.RunOnStartup) startup.Register(); else startup.Unregister();

        _controller = _services.GetRequiredService<ISwitchController>();
        await _controller.StartAsync();  // performs startup revert if needed

        SystemEvents.SessionEnding    += OnSessionEnding;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        // Resolve and keep alive; TrayIconView is declared as a resource in App.xaml
        _ = _services.GetRequiredService<TrayIconViewModel>();
    }

    private void OnSessionEnding(object sender, SessionEndingEventArgs e)
        => _controller.RevertNow(SwitchReason.SessionEnding);

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        // On resume, the next ActivityTracker poll will re-read the display topology.
        // No explicit action needed; SwitchController will reconcile on the next tick.
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.SessionEnding    -= OnSessionEnding;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _controller.Dispose();
        _services.Dispose();
        base.OnExit(e);
    }
}
```

`App.xaml` sets `ShutdownMode="OnExplicitShutdown"` with no `StartupUri`. The `TaskbarIcon` (from `Hardcodet.Wpf.TaskbarNotification`) is declared as a resource in `App.xaml` and bound to `TrayIconViewModel`.

---

## 8. DI Registration Summary

| Interface | Implementation | Lifetime | Reason |
|---|---|---|---|
| `IConfigurationService` | `ConfigurationService` | Singleton | Single source of truth for config |
| `IDisplayService` | `DisplayService` | Singleton | Stateless Win32 calls; no benefit to multiple instances |
| `IAudioService` | `AudioService` | Singleton | Stateless COM calls |
| `IWindowEventSource` | `WindowEventSource` | Singleton | Single WinEventHook per process |
| `IActivityTracker` | `ActivityTracker` | Singleton | Single polling loop |
| `ISwitchController` | `SwitchController` | Singleton | Owns the state machine |
| `INotificationService` | `NotificationService` | Singleton | Stateless toast calls |
| `IStartupService` | `StartupService` | Singleton | Registry reads are cheap; no state |
| `TrayIconViewModel` | — | Singleton | Lives for the app lifetime |
| `SettingsViewModel` | — | Transient | Fresh device enumeration on each open |
| `Dispatcher` | `Application.Current.Dispatcher` | Singleton | WPF UI thread dispatcher |

---

## 9. Threading Model

```
┌──────────────────────────────────────────────────────────────┐
│  UI Thread (WPF Dispatcher / Win32 message loop)             │
│                                                              │
│  TrayIconViewModel, TrayIconView                             │
│  SettingsViewModel, SettingsWindow                           │
│  WindowEventSource._hookProc  (WinEventHook fires here)      │
│  ISwitchController events (marshaled here via InvokeAsync)   │
│  App.OnSessionEnding (synchronous revert call)               │
└──────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────┐
│  Background Task (ActivityTracker.PollLoopAsync)             │
│                                                              │
│  PeriodicTimer loop on Task.Run                              │
│  GetCursorPos / GetForegroundWindow / MonitorFromPoint        │
│  IActivityTracker.SampleProduced fires here                  │
│  SwitchController.OnSampleProduced handles here (stateLock)  │
│  ExecuteSwitch calls IDisplayService + IAudioService here    │
│  UI-visible state changes marshaled via Dispatcher.InvokeAsync│
└──────────────────────────────────────────────────────────────┘
```

**Marshaling**: After computing a state change, `SwitchController` calls:
```csharp
await _uiDispatcher.InvokeAsync(() => {
    StateChanged?.Invoke(this, _currentState);
    SwitchCompleted?.Invoke(this, (direction, reason, result));
});
```

**Session-ending path**: `RevertNow` is called synchronously on the UI thread from `App.OnSessionEnding`. It acquires `_stateLock` (the background task holds it for at most one poll tick, ≤ 1 second elevated / 5 seconds normal). No async, no awaits.

**COM threading**: `AudioService` creates COM objects via `Activator.CreateInstance`. COM is implicitly initialized on the background task thread as MTA (the default for `Task.Run` threads). `AudioPolicyConfigFactory` and `MMDeviceEnumeratorClass` are both free-threaded / apartment-neutral and work correctly from MTA.

---

## 10. Key Design Decisions

**`SwitchController` owns the state machine entirely** — all counter tracking, transition logic, and coordination live in one class under one lock. Distributing this across services would require cross-component synchronization with no architectural benefit.

**Elevated polling uses an expiry timestamp, not a second timer** — `ActivityTracker` checks `DateTimeOffset.UtcNow < _elevatedExpiry` at the top of each tick. When the interval must change, it cancels and restarts the `PeriodicTimer`. This keeps the timer management simple and race-free.

**`AppConfiguration` is an immutable record** — `ConfigurationService.Save` replaces the entire `Current` reference atomically. Observers reading `Current` always see a consistent snapshot; no partial-update races are possible.

**`SettingsViewModel` is Transient** — each time the settings window is opened, a fresh instance calls `GetActiveMonitors()` and `GetPlaybackEndpoints()`, picking up topology changes since the last open without needing a refresh mechanism.

**`RevertNow` is synchronous** — the `WM_ENDSESSION` budget (~5 seconds) and the `QUERYENDSESSION` response requirement make async impractical. `SetDisplayConfig` and `IAudioPolicyConfig.SetDefaultEndpoint` are both fast Win32/COM calls; the synchronous path is safe.

**`IAudioPolicyConfig` used exclusively; `IPolicyConfig` dropped** — `IAudioPolicyConfig` (available Windows 10 1803+) has a shorter, better-verified vtable. The app requires Windows 10 1803+ as its minimum OS. The older `IPolicyConfig` fallback is not included.

**`SetDefaultEndpoint` called for all three `ERole` values** — calling only `eConsole` leaves multimedia and communication audio on the old device. Setting all three (eConsole, eMultimedia, eCommunications) ensures a complete audio switch, matching EarTrumpet's behaviour.

**`WindowEventSource` must be started on the UI thread** — `SetWinEventHook` requires the calling thread to have a Win32 message loop. WPF's Dispatcher thread satisfies this. The hook callback therefore fires on the UI thread as part of normal WPF message dispatch — no extra thread or marshaling needed for the `WindowMovedToHdtv` event.

**`MonitorInfo.DevicePath` uses the stable `\\?\DISPLAY#...` form** — the `\\.\DISPLAYn` number from `EnumDisplayDevices` shifts after reconnects. The device path from `DISPLAYCONFIG_TARGET_DEVICE_NAME.monitorDevicePath` is stable for a given physical connector and survives reboots. `FriendlyName` (EDID model) is the secondary fallback in `ResolveTargetDesktopMonitor`.

---

## 11. NuGet Dependencies

| Package | Purpose |
|---|---|
| `Microsoft.Windows.CsWin32` | Source generator for all documented Win32 P/Invoke declarations and structs (including `DISPLAYCONFIG_*`, `MONITORINFOEX`, `WINEVENTPROC`, cursor/window APIs) |
| `Hardcodet.Wpf.TaskbarNotification` | WPF-native system tray icon with MVVM binding |
| `CommunityToolkit.Mvvm` | `ObservableObject`, `RelayCommand`, `[ObservableProperty]` source gen |
| `Microsoft.Extensions.DependencyInjection` | DI container |
| `Microsoft.Toolkit.Uwp.Notifications` | Toast notification builder (wraps WinRT) |

All other dependencies (`System.Text.Json`, `System.Windows.Forms` for `SystemEvents`) ship with .NET 10.

### CsWin32 configuration

CsWin32 requires a `NativeMethods.txt` file at the project root listing the APIs
to generate, and the following in the `.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Windows.CsWin32" Version="*-prerelease"
                    PrivateAssets="all" />
</ItemGroup>
```

Generated types land in the `Windows.Win32` namespace and are `internal` by
default. No generated file should be edited — regenerate by updating
`NativeMethods.txt` and rebuilding.
