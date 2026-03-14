using System.Runtime.InteropServices;

namespace DefaultMonitorSwitcher.Infrastructure.Audio;

// IAudioPolicyConfig — undocumented Windows internal interface available on
// Windows 10 1803+. Preferred over the older IPolicyConfig because its vtable
// is shorter and better verified by open-source projects (notably EarTrumpet).
// IID sourced from EarTrumpet and cross-verified against Windows.Media.Audio.dll symbols.
// IMPORTANT: Verify slot count and SetDefaultEndpoint index against EarTrumpet
//            AudioPolicyConfig.cs before shipping. The NotImpl stubs are placeholders.
[ComImport]
[Guid("2a59116d-6c4f-45e0-a74f-707e3fef9258")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioPolicyConfig
{
    void NotImpl1();
    void NotImpl2();
    void NotImpl3();
    void NotImpl4();
    void NotImpl5();
    void NotImpl6();
    void NotImpl7();
    void NotImpl8();
    void NotImpl9();
    void NotImpl10();

    [PreserveSig]
    int SetDefaultEndpoint(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
        ERole role);
}

[ComImport]
[Guid("1776DCD9-FA97-4463-A8B3-AD4B5C5DAD27")]
internal class AudioPolicyConfigFactory { }

internal enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }
