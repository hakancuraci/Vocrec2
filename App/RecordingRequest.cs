using SesKayit.Core;

namespace SesKayit;

internal sealed record RecordingRequest(DeviceChoice[] Sources, string Folder, int Bitrate, float SystemGain, float MicGain, bool Automatic, bool EnhanceMicrophone = true);

public interface IRecordingSession : IDisposable
{
    bool Automatic { get; set; }
    AudioMixer Mixer { get; }
    MixerInput[] Inputs { get; }
    Task Completion { get; }
    string OutputPath { get; }
    string PartialPath { get; }
    Exception? CaptureError { get; }
    string? Warning { get; }
    void SyncSources(IEnumerable<DeviceChoice> choices);
    Task StopAsync();
}
