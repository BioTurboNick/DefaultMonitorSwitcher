using System.Runtime.InteropServices;
using DefaultMonitorSwitcher.Core;

namespace DefaultMonitorSwitcher.Infrastructure.Audio;

public sealed class AudioService : IAudioService
{
    private const uint DEVICE_STATE_ACTIVE = 0x00000001;
    private const uint STGM_READ           = 0x00000000;
    private const uint CLSCTX_INPROC_SERVER = 0x1;

    // ── Endpoint enumeration ─────────────────────────────────────────────────

    public IReadOnlyList<AudioEndpointInfo> GetPlaybackEndpoints()
    {
        var enumerator = CreateEnumerator();
        int hr = enumerator.EnumAudioEndpoints(EDataFlow.eRender, DEVICE_STATE_ACTIVE, out var collection);
        if (hr != 0)
            return [];

        hr = collection.GetCount(out uint count);
        if (hr != 0)
            return [];

        var result = new List<AudioEndpointInfo>((int)count);
        for (uint i = 0; i < count; i++)
        {
            hr = collection.Item(i, out var device);
            if (hr != 0) continue;

            var info = TryReadEndpointInfo(device);
            if (info != null)
                result.Add(info);
        }
        return result;
    }

    public string? GetDefaultPlaybackDeviceId()
    {
        var enumerator = CreateEnumerator();
        int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eConsole, out var device);
        if (hr != 0 || device == null)
            return null;

        hr = device.GetId(out string? id);
        return hr == 0 ? id : null;
    }

    public AudioEndpointInfo? AutoDetectEndpointForMonitor(MonitorInfo monitor)
    {
        var endpoints = GetPlaybackEndpoints();
        // Case-insensitive substring match: monitor friendly name inside endpoint friendly name
        return endpoints.FirstOrDefault(e =>
            e.FriendlyName.Contains(monitor.FriendlyName, StringComparison.OrdinalIgnoreCase));
    }

    // ── Set default device ───────────────────────────────────────────────────

    public bool TrySetDefaultPlaybackDevice(string deviceId, out string? failureReason)
    {
        IPolicyConfig policy;
        try
        {
            policy = (IPolicyConfig)new CPolicyConfigClient();
        }
        catch (Exception ex)
        {
            failureReason = $"CoCreateInstance(CPolicyConfigClient) failed: {ex.Message}";
            return false;
        }

        foreach (ERole role in new[] { ERole.eConsole, ERole.eMultimedia, ERole.eCommunications })
        {
            int hr = policy.SetDefaultEndpoint(deviceId, role);
            if (hr != 0)
            {
                failureReason = $"SetDefaultEndpoint({role}) failed: 0x{hr:X8}";
                return false;
            }
        }

        failureReason = null;
        return true;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static IMMDeviceEnumerator CreateEnumerator() =>
        (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();

    private static AudioEndpointInfo? TryReadEndpointInfo(IMMDevice device)
    {
        int hr = device.GetId(out string? id);
        if (hr != 0 || id == null) return null;

        string friendlyName = id; // fallback

        hr = device.OpenPropertyStore(STGM_READ, out var store);
        if (hr == 0 && store != null)
        {
            var key = PropertyKeys.PKEY_Device_FriendlyName;
            hr = store.GetValue(ref key, out PROPVARIANT pv);
            if (hr == 0 && pv.vt == 31 /*VT_LPWSTR*/ && pv.value != IntPtr.Zero)
            {
                friendlyName = Marshal.PtrToStringUni(pv.value) ?? id;
            }
        }

        return new AudioEndpointInfo { DeviceId = id, FriendlyName = friendlyName };
    }
}
