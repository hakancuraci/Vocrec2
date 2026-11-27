using NAudio.CoreAudioApi;
using SesKayit.Core;

namespace SesKayit;

public static class AudioDeviceCatalog
{
    // Enumerate endpoints only. Never inspect application sessions on the recording-start path.
    public static ObservedDevice[] Read()
    {
        var result = new List<ObservedDevice>();
        using var enumerator = new MMDeviceEnumerator();
        foreach (var flow in new[] { DataFlow.Render, DataFlow.Capture })
        {
            string? defaultId = null;
            string? playbackId = null;
            try { using var device = enumerator.GetDefaultAudioEndpoint(flow, Role.Communications); defaultId = device.ID; }
            catch { /* No default endpoint is valid on machines without audio devices. */ }
            if (flow == DataFlow.Render)
            {
                try { using var device = enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia); playbackId = device.ID; }
                catch { /* No multimedia endpoint. */ }
            }
            foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            {
                using (device)
                {
                    try
                    {
                        float peak = 0; string format = "";
                        if (flow == DataFlow.Render)
                            try { peak = device.AudioMeterInformation.MasterPeakValue; } catch { /* Meter can be unavailable during a jack transition. */ }
                        try
                        {
                            if (device.Properties[PropertyKeys.PKEY_AudioEngine_DeviceFormat].Value is byte[] bytes) format = Convert.ToHexString(bytes);
                        }
                        catch { /* Format may briefly be unavailable during reconfiguration. */ }
                        result.Add(new ObservedDevice(device.ID, device.FriendlyName, flow == DataFlow.Capture, device.ID == defaultId, false,
                            device.ID == playbackId, peak, format));
                    }
                    catch { /* It may have been unplugged since enumeration. */ }
                }
            }
        }
        return result.ToArray();
    }
}
