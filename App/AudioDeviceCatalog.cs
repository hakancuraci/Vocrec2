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
            try { using var device = enumerator.GetDefaultAudioEndpoint(flow, Role.Communications); defaultId = device.ID; }
            catch { /* No default endpoint is valid on machines without audio devices. */ }
            foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            {
                using (device)
                {
                    try { result.Add(new ObservedDevice(device.ID, device.FriendlyName, flow == DataFlow.Capture, device.ID == defaultId, false)); }
                    catch { /* It may have been unplugged since enumeration. */ }
                }
            }
        }
        return result.ToArray();
    }
}
