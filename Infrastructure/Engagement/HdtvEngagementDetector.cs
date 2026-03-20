using System.Runtime.InteropServices;
using DefaultMonitorSwitcher.Core;
using DefaultMonitorSwitcher.Infrastructure.Audio;
using Windows.Media.Control;

namespace DefaultMonitorSwitcher.Infrastructure.Engagement;

/// <summary>
/// Detects whether the HDTV is actively in use via three supplemental signals (§4.4):
///   1. DXGI Desktop Duplication — new frames being presented to the HDTV output
///   2. WASAPI audio peak meter  — non-zero audio output on the HDTV audio endpoint
///   3. Windows Media Session    — any SMTC-registered app reporting PlaybackStatus.Playing
///
/// Used by ActivityTracker to populate ActivitySample.IsHdtvEngaged, which the
/// SwitchController uses to prevent idle reverts while a game or media app is active.
/// </summary>
public sealed class HdtvEngagementDetector : IHdtvEngagementDetector
{
    private readonly IConfigurationService _configService;
    private readonly Lock _lock = new();

    private MonitorInfo? _hdtvMonitor;
    private string?      _hdtvAudioDeviceId;

    // ── DXGI state ────────────────────────────────────────────────────────────

    private IDXGIOutputDuplication? _duplication;
    private nint _d3dDevice;
    private nint _d3dContext;
    private DateTimeOffset _duplicationRetryAfter = DateTimeOffset.MinValue;

    // ── Audio state ───────────────────────────────────────────────────────────

    private IAudioMeterInformation? _meter;
    private DateTimeOffset _meterRetryAfter = DateTimeOffset.MinValue;

    // ── SMTC media session state ───────────────────────────────────────────────

    private GlobalSystemMediaTransportControlsSessionManager? _smtcManager;

    // ── Construction ──────────────────────────────────────────────────────────

    public HdtvEngagementDetector(IConfigurationService configService)
    {
        _configService = configService;
        _ = InitSmtcAsync();
    }

    // ── IHdtvEngagementDetector ───────────────────────────────────────────────

    public bool IsEngaged()
    {
        if (!_configService.Current.HdtvEngagementDetectionEnabled)
            return false;

        lock (_lock)
        {
            if (_hdtvMonitor == null)
                return false;

            return CheckDxgiFrameActivity() || CheckAudioActivity() || CheckMediaSessionActivity();
        }
    }

    public void Configure(MonitorInfo? hdtvMonitor, string? hdtvAudioDeviceId)
    {
        lock (_lock)
        {
            bool monitorChanged = hdtvMonitor?.DevicePath != _hdtvMonitor?.DevicePath;
            bool audioChanged   = hdtvAudioDeviceId != _hdtvAudioDeviceId;

            _hdtvMonitor       = hdtvMonitor;
            _hdtvAudioDeviceId = hdtvAudioDeviceId;

            if (monitorChanged)
            {
                ReleaseDuplication();
                _duplicationRetryAfter = DateTimeOffset.MinValue;
            }

            if (audioChanged)
            {
                _meter             = null;
                _meterRetryAfter   = DateTimeOffset.MinValue;
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            ReleaseDuplication();
            _meter = null;
        }
    }

    // ── SMTC media session detection ──────────────────────────────────────────

    private async Task InitSmtcAsync()
    {
        try
        {
            var mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            lock (_lock) { _smtcManager = mgr; }
        }
        catch { /* SMTC unavailable (requires Windows 10 1903+); signals remain inactive */ }
    }

    private bool CheckMediaSessionActivity()
    {
        if (_smtcManager == null) return false;
        try
        {
            foreach (var session in _smtcManager.GetSessions())
            {
                if (session.GetPlaybackInfo()?.PlaybackStatus ==
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    return true;
            }
        }
        catch { }
        return false;
    }

    // ── DXGI frame detection ──────────────────────────────────────────────────

    private bool CheckDxgiFrameActivity()
    {
        EnsureDuplication();
        if (_duplication == null) return false;

        const int DXGI_ERROR_ACCESS_LOST = unchecked((int)0x887A0026);

        int hr = _duplication.AcquireNextFrame(0, out DxgiOutduplFrameInfo frameInfo, out nint ppResource);

        if (hr == 0)
        {
            if (ppResource != nint.Zero) Marshal.Release(ppResource);
            _duplication.ReleaseFrame();
            // LastPresentTime is non-zero only when desktop content was updated.
            // A zero value means only the cursor changed — not meaningful engagement.
            return frameInfo.LastPresentTime != 0;
        }

        if (hr == DXGI_ERROR_ACCESS_LOST)
        {
            ReleaseDuplication();
            _duplicationRetryAfter = DateTimeOffset.UtcNow.AddSeconds(5);
        }

        return false;
    }

    private void EnsureDuplication()
    {
        if (_duplication != null) return;
        if (DateTimeOffset.UtcNow < _duplicationRetryAfter) return;
        if (_hdtvMonitor == null) return;

        try
        {
            // Create D3D11 device once; reuse across HDTV monitor changes.
            if (_d3dDevice == nint.Zero)
            {
                const int  D3D_DRIVER_TYPE_HARDWARE      = 1;
                const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
                const uint D3D11_SDK_VERSION             = 7;

                int hr = DxgiNative.D3D11CreateDevice(
                    nint.Zero, D3D_DRIVER_TYPE_HARDWARE, nint.Zero,
                    D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                    nint.Zero, 0, D3D11_SDK_VERSION,
                    out _d3dDevice, out _, out _d3dContext);

                if (hr != 0)
                {
                    _d3dDevice = _d3dContext = nint.Zero;
                    _duplicationRetryAfter = DateTimeOffset.UtcNow.AddSeconds(30);
                    return;
                }
            }

            // Create DXGI factory.
            var factoryIid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); // IDXGIFactory1
            if (DxgiNative.CreateDXGIFactory1(in factoryIid, out nint ppFactory) != 0
                || ppFactory == nint.Zero)
            {
                _duplicationRetryAfter = DateTimeOffset.UtcNow.AddSeconds(30);
                return;
            }

            var factory = (IDXGIFactory)Marshal.GetObjectForIUnknown(ppFactory);
            Marshal.Release(ppFactory);

            // Enumerate adapters → outputs, find the one whose DesktopCoordinates
            // match the HDTV monitor bounds.
            var hdtvBounds = _hdtvMonitor.Bounds;
            IDXGIOutput1? hdtvOutput1 = null;

            const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);

            for (uint a = 0; factory.EnumAdapters(a, out IDXGIAdapter adapter) != DXGI_ERROR_NOT_FOUND; a++)
            {
                for (uint o = 0; adapter.EnumOutputs(o, out IDXGIOutput output) != DXGI_ERROR_NOT_FOUND; o++)
                {
                    if (output.GetDesc(out DxgiOutputDesc desc) == 0
                        && desc.Left   == hdtvBounds.Left
                        && desc.Top    == hdtvBounds.Top
                        && desc.Right  == hdtvBounds.Right
                        && desc.Bottom == hdtvBounds.Bottom)
                    {
                        hdtvOutput1 = output as IDXGIOutput1;
                        break;
                    }
                }
                if (hdtvOutput1 != null) break;
            }

            if (hdtvOutput1 == null)
            {
                _duplicationRetryAfter = DateTimeOffset.UtcNow.AddSeconds(30);
                return;
            }

            // DuplicateOutput requires a D3D11 device.
            int dhr = hdtvOutput1.DuplicateOutput(_d3dDevice, out nint ppDup);
            if (dhr != 0 || ppDup == nint.Zero)
            {
                _duplicationRetryAfter = DateTimeOffset.UtcNow.AddSeconds(30);
                return;
            }

            _duplication = (IDXGIOutputDuplication)Marshal.GetObjectForIUnknown(ppDup);
            Marshal.Release(ppDup);
        }
        catch
        {
            ReleaseDuplication();
            _duplicationRetryAfter = DateTimeOffset.UtcNow.AddSeconds(30);
        }
    }

    private void ReleaseDuplication()
    {
        if (_duplication != null)
        {
            Marshal.ReleaseComObject(_duplication);
            _duplication = null;
        }
        if (_d3dDevice != nint.Zero)
        {
            Marshal.Release(_d3dDevice);
            _d3dDevice = nint.Zero;
        }
        if (_d3dContext != nint.Zero)
        {
            Marshal.Release(_d3dContext);
            _d3dContext = nint.Zero;
        }
    }

    // ── WASAPI audio peak detection ───────────────────────────────────────────

    private bool CheckAudioActivity()
    {
        EnsureAudioMeter();
        if (_meter == null) return false;

        try
        {
            _meter.GetPeakValue(out float peak);
            return peak > 0f;
        }
        catch
        {
            _meter           = null;
            _meterRetryAfter = DateTimeOffset.UtcNow.AddSeconds(10);
            return false;
        }
    }

    private void EnsureAudioMeter()
    {
        if (_meter != null) return;
        if (DateTimeOffset.UtcNow < _meterRetryAfter) return;
        if (_hdtvAudioDeviceId == null) return;

        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
            if (enumerator.GetDevice(_hdtvAudioDeviceId, out IMMDevice device) != 0)
            {
                _meterRetryAfter = DateTimeOffset.UtcNow.AddSeconds(10);
                return;
            }

            var meterIid = new Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"); // IAudioMeterInformation
            if (device.Activate(ref meterIid, 0x17 /* CLSCTX_ALL */, nint.Zero, out nint ppMeter) != 0
                || ppMeter == nint.Zero)
            {
                _meterRetryAfter = DateTimeOffset.UtcNow.AddSeconds(10);
                return;
            }

            _meter = (IAudioMeterInformation)Marshal.GetObjectForIUnknown(ppMeter);
            Marshal.Release(ppMeter);
        }
        catch
        {
            _meter           = null;
            _meterRetryAfter = DateTimeOffset.UtcNow.AddSeconds(10);
        }
    }
}

// ── DXGI native methods ───────────────────────────────────────────────────────

internal static class DxgiNative
{
    [DllImport("d3d11.dll", PreserveSig = true)]
    internal static extern int D3D11CreateDevice(
        nint   pAdapter,
        int    DriverType,
        nint   Software,
        uint   Flags,
        nint   pFeatureLevels,
        uint   FeatureLevels,
        uint   SDKVersion,
        out nint ppDevice,
        out int  pFeatureLevel,
        out nint ppImmediateContext);

    [DllImport("dxgi.dll", PreserveSig = true)]
    internal static extern int CreateDXGIFactory1(
        in Guid riid,
        out nint ppFactory);
}

// ── DXGI COM interfaces ───────────────────────────────────────────────────────
// Vtable layout: IUnknown (slots 0-2, handled by CLR) + IDXGIObject (slots 3-6)
// + interface-specific methods. Unneeded methods stubbed with void to maintain
// vtable alignment.

[ComImport]
[Guid("7B7166EC-21C7-44AE-B21A-C9AE321AE369")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDXGIFactory
{
    // IDXGIObject (slots 3-6)
    void SetPrivateData_();
    void SetPrivateDataInterface_();
    void GetPrivateData_();
    void GetParent_();
    // IDXGIFactory (slots 7-11)
    void MakeWindowAssociation_();
    void GetWindowAssociation_();
    void CreateSwapChain_();
    void CreateSoftwareAdapter_();
    [PreserveSig] int EnumAdapters(uint Adapter, out IDXGIAdapter ppAdapter);
}

[ComImport]
[Guid("2411E7E1-12AC-4CCF-BD14-9798E8534DC0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDXGIAdapter
{
    // IDXGIObject (slots 3-6)
    void SetPrivateData_();
    void SetPrivateDataInterface_();
    void GetPrivateData_();
    void GetParent_();
    // IDXGIAdapter (slots 7-9)
    [PreserveSig] int EnumOutputs(uint Output, out IDXGIOutput ppOutput);
    void GetDesc_();
    void CheckInterfaceSupport_();
}

[ComImport]
[Guid("AE02EEDB-C735-4690-8D52-5A8DC20213AA")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDXGIOutput
{
    // IDXGIObject (slots 3-6)
    void SetPrivateData_();
    void SetPrivateDataInterface_();
    void GetPrivateData_();
    void GetParent_();
    // IDXGIOutput (slots 7-18)
    [PreserveSig] int GetDesc(out DxgiOutputDesc pDesc);
    void GetDisplayModeList_();
    void FindClosestMatchingMode_();
    void WaitForVBlank_();
    void TakeOwnership_();
    void ReleaseOwnership_();
    void GetGammaControlCapabilities_();
    void SetGammaControl_();
    void GetGammaControl_();
    void SetDisplaySurface_();
    void GetDisplaySurfaceData_();
    void GetFrameStatistics_();
}

[ComImport]
[Guid("00cddea8-939b-4b83-a340-a685226666cc")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDXGIOutput1
{
    // IDXGIObject (slots 3-6)
    void SetPrivateData_();
    void SetPrivateDataInterface_();
    void GetPrivateData_();
    void GetParent_();
    // IDXGIOutput (slots 7-18)
    [PreserveSig] int GetDesc(out DxgiOutputDesc pDesc);
    void GetDisplayModeList_();
    void FindClosestMatchingMode_();
    void WaitForVBlank_();
    void TakeOwnership_();
    void ReleaseOwnership_();
    void GetGammaControlCapabilities_();
    void SetGammaControl_();
    void GetGammaControl_();
    void SetDisplaySurface_();
    void GetDisplaySurfaceData_();
    void GetFrameStatistics_();
    // IDXGIOutput1 (slots 19-22)
    void GetDisplayModeList1_();
    void FindClosestMatchingMode1_();
    void GetDisplaySurfaceData1_();
    [PreserveSig] int DuplicateOutput(nint pDevice, out nint ppOutputDuplication);
}

[ComImport]
[Guid("191cfac3-a341-470d-b26e-a864f428319c")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDXGIOutputDuplication
{
    // IDXGIObject (slots 3-6)
    void SetPrivateData_();
    void SetPrivateDataInterface_();
    void GetPrivateData_();
    void GetParent_();
    // IDXGIOutputDuplication (slots 7-14)
    void GetDesc_();
    [PreserveSig] int AcquireNextFrame(uint TimeoutInMilliseconds, out DxgiOutduplFrameInfo pFrameInfo, out nint ppDesktopResource);
    void GetFrameDirtyRects_();
    void GetFrameMoveRects_();
    void GetFramePointerShape_();
    void MapDesktopSurface_();
    void UnMapDesktopSurface_();
    [PreserveSig] int ReleaseFrame();
}

// ── DXGI structs ──────────────────────────────────────────────────────────────

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DxgiOutputDesc
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string DeviceName;      // 64 bytes
    public int Left;               // DesktopCoordinates.left
    public int Top;                // DesktopCoordinates.top
    public int Right;              // DesktopCoordinates.right
    public int Bottom;             // DesktopCoordinates.bottom
    public int AttachedToDesktop;  // BOOL
    public uint Rotation;          // DXGI_MODE_ROTATION
    public nint Monitor;           // HMONITOR
}

[StructLayout(LayoutKind.Sequential)]
internal struct DxgiOutduplFrameInfo
{
    public long LastPresentTime;             // LARGE_INTEGER
    public long LastMouseUpdateTime;         // LARGE_INTEGER
    public uint AccumulatedFrames;
    public uint RectsCoalesced;              // BOOL
    public uint ProtectedContentMaskedOut;   // BOOL
    public int  PointerPositionX;
    public int  PointerPositionY;
    public uint PointerPositionVisible;      // BOOL
    public uint TotalMetadataBufferSize;
    public uint PointerShapeBufferSize;
}
