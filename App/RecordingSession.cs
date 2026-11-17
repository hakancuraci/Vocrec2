using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SesKayit.Core;

namespace SesKayit;

public sealed record DeviceChoice(string Id, string Name, bool Microphone)
{
    public override string ToString() => Name;
}

public sealed class RecordingSession : IDisposable
{
    private readonly List<WasapiCapture> captures = [];
    private readonly List<MMDevice> devices = [];
    private readonly List<BufferedWaveProvider> buffers = [];
    private readonly MMDeviceEnumerator enumerator = new();
    private volatile bool stopping;
    private Exception? captureError;
    public AudioMixer Mixer { get; private set; } = null!;
    public MixerInput[] Inputs { get; private set; } = [];
    public Task Completion { get; private set; } = Task.CompletedTask;
    public string OutputPath { get; }
    public string PartialPath { get; }
    public Exception? CaptureError => Volatile.Read(ref captureError);

    public RecordingSession(IEnumerable<DeviceChoice> choices, string folder, int bitrate, float systemGain, float micGain)
    {
        Directory.CreateDirectory(folder);
        string name = $"Kayit_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{Guid.NewGuid().ToString("N")[..6]}";
        OutputPath = Path.Combine(folder, name + ".m4a");
        PartialPath = Path.Combine(folder, name + ".partial.m4a");
        try
        {
            // Early check: no long recording may be made without a working AAC encoder.
            var rates = MediaFoundationEncoder.GetEncodeBitrates(NAudio.MediaFoundation.AudioSubtypes.MFAudioFormat_AAC, 48000, 2);
            if (!rates.Contains(bitrate)) throw new InvalidOperationException("Bu AAC kalitesi sistemde desteklenmiyor. Başka kalite seçin. Windows N için Media Feature Pack gerekebilir.");
            var inputs = new List<MixerInput>();
            foreach (var choice in choices)
            {
                var device = enumerator.GetDevice(choice.Id);
                devices.Add(device);
                WasapiCapture capture = choice.Microphone ? new WasapiCapture(device) : new WasapiLoopbackCapture(device);
                captures.Add(capture);
                var buffered = new BufferedWaveProvider(capture.WaveFormat)
                {
                    BufferDuration = TimeSpan.FromSeconds(3), ReadFully = true, DiscardOnBufferOverflow = false
                };
                buffers.Add(buffered);
                capture.DataAvailable += (_, e) =>
                {
                    try { buffered.AddSamples(e.Buffer, 0, e.BytesRecorded); }
                    catch (Exception ex) { Interlocked.CompareExchange(ref captureError,
                        new IOException($"Ses tamponu işlenemedi: {choice.Name}. Disk veya işlemci yetişemiyor olabilir.", ex), null); }
                };
                capture.RecordingStopped += (_, e) =>
                {
                    if (!stopping) Interlocked.CompareExchange(ref captureError,
                        e.Exception ?? new IOException($"Ses kaynağı durdu: {choice.Name}"), null);
                };
                ISampleProvider samples = new StereoSource(buffered.ToSampleProvider());
                if (samples.WaveFormat.SampleRate != AudioMixer.Rate)
                    samples = new WdlResamplingSampleProvider(samples, AudioMixer.Rate);
                inputs.Add(new MixerInput(samples, choice.Microphone));
            }
            Inputs = inputs.ToArray();
            Mixer = new AudioMixer(Inputs) { SystemGain = systemGain, MicrophoneGain = micGain };
            foreach (var capture in captures) capture.StartRecording();
            Completion = Task.Run(() => MediaFoundationEncoder.EncodeToAac(Mixer, PartialPath, bitrate));
        }
        catch { Dispose(); throw; }
    }

    public async Task StopAsync()
    {
        stopping = true;
        Exception? stopError = null;
        foreach (var capture in captures)
            try { capture.StopRecording(); } catch (Exception ex) { stopError ??= ex; }
        // Drain endpoint latency before signalling EOF, so the last words aren't cut off.
        int drainMs = (int)Math.Ceiling(buffers.Select(b => b.BufferedDuration.TotalMilliseconds).DefaultIfEmpty().Max()) + 100;
        await Task.WhenAny(Completion, Task.Delay(drainMs));
        Mixer.Finish();
        await Completion;
        File.Move(PartialPath, OutputPath);
        if (CaptureError != null || stopError != null)
            throw new IOException($"Kayıt erken durdu. Tamamlanan bölüm kaydedildi:\n{OutputPath}", CaptureError ?? stopError);
    }

    public void Dispose()
    {
        stopping = true;
        Mixer?.Finish();
        foreach (var capture in captures) { try { capture.Dispose(); } catch { /* Continue releasing other endpoints. */ } }
        foreach (var device in devices) device.Dispose();
        enumerator.Dispose();
        // Only dispose the wait handle once the encoder has left Read().
        if (Completion.IsCompleted) Mixer?.Dispose();
    }
}
