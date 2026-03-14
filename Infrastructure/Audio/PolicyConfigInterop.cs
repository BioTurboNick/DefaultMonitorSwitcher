using System.Runtime.InteropServices;

namespace DefaultMonitorSwitcher.Infrastructure.Audio;

// IPolicyConfig — undocumented but stable Windows COM interface for setting
// the system-wide default audio endpoint.  Routes through AudioSRV RPC,
// the same path used by Windows Sound Control Panel.
//
// IID / CLSID sourced from SoundSwitch, AudioDeviceCmdlets, and AudioEndPointLibrary —
// all actively maintained as of 2024-2025 and confirmed working on Windows 11 25H2.
//
// Usage: CoCreateInstance(CPolicyConfigClient) → cast to IPolicyConfig
//        → SetDefaultEndpoint(rawDeviceId, role)  for each ERole value.
// rawDeviceId: the string returned by IMMDevice::GetId(),
//              e.g. "{0.0.0.00000000}.{guid}"

[ComImport]
[Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    // Slots 3–12: unneeded methods — kept as stubs to preserve vtable layout
    void GetMixFormat();
    void GetDeviceFormat();
    void ResetDeviceFormat();
    void SetDeviceFormat();
    void GetProcessingPeriod();
    void SetProcessingPeriod();
    void GetShareMode();
    void SetShareMode();
    void GetPropertyValue();
    void SetPropertyValue();

    // Slot 13
    [PreserveSig]
    int SetDefaultEndpoint(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
        ERole role);

    // Slot 14 — not used, kept to document the interface
    [PreserveSig]
    int SetEndpointVisibility(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
        [MarshalAs(UnmanagedType.Bool)] bool visible);
}

[ComImport]
[Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
internal class CPolicyConfigClient { }

internal enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }
