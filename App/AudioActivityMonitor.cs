using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using SesKayit.Core;

namespace SesKayit;

public sealed record ActivitySnapshot(ObservedDevice[] Devices, string[] Outputs, string[] Microphones, string[] Warnings);

public static class AudioActivityMonitor
{
    public static ActivitySnapshot Scan()
    {
        var devices = new List<ObservedDevice>();
        var outputs = new List<string>();
        var microphones = new List<string>();
        var warnings = new List<string>();
        using var enumerator = new MMDeviceEnumerator();
        foreach (var flow in new[] { DataFlow.Render, DataFlow.Capture })
        {
            bool mic = flow == DataFlow.Capture;
            string? defaultId = null;
            try { using var device = enumerator.GetDefaultAudioEndpoint(flow, Role.Communications); defaultId = device.ID; }
            catch { /* Machines may have no default device. */ }
            foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            {
                using (device)
                {
                    bool used = false;
                    string id, name;
                    try { id = device.ID; name = device.FriendlyName; }
                    catch { continue; } // Unplugged between enumeration and inspection.
                    try
                    {
                        var manager = device.AudioSessionManager;
                        manager.RefreshSessions();
                        var sessions = manager.Sessions;
                        for (int i = 0; i < sessions.Count; i++)
                        {
                            using var session = sessions[i];
                            try
                            {
                                uint pid = session.GetProcessID;
                                if (pid == Environment.ProcessId || session.State != AudioSessionState.AudioSessionStateActive) continue;
                                used = true;
                                string app = ProcessName(pid);
                                float peak = session.AudioMeterInformation.MasterPeakValue;
                                string activity = peak > 0.001f ? "ses var" : (mic ? "mikrofonu kullanıyor · ses yok" : "oturum açık · ses yok");
                                (mic ? microphones : outputs).Add($"{app} · {activity} · {name}");
                            }
                            catch { /* A process may close while its session is being read. */ }
                        }
                    }
                    catch { warnings.Add($"Oturum bilgisi okunamadı: {name}"); }
                    devices.Add(new ObservedDevice(id, name, mic, id == defaultId, used));
                }
            }
        }
        return new ActivitySnapshot(devices.ToArray(), outputs.Distinct().ToArray(), microphones.Distinct().ToArray(), warnings.ToArray());
    }

    private static string ProcessName(uint pid)
    {
        if (pid == 0) return "Windows sistem sesleri";
        try { using var process = Process.GetProcessById((int)pid); return process.ProcessName; }
        catch { return $"Uygulama {pid}"; }
    }
}
