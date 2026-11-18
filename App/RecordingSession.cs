using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SesKayit.Core;

namespace SesKayit;

public sealed record DeviceChoice(string Id, string Name, bool Microphone)
{
    public override string ToString() => Name;
}

public sealed class RecordingSession : IRecordingSession
{
    private readonly Dictionary<string, SourceCapture> sources = [];
    private readonly MMDeviceEnumerator enumerator = new();
    private bool stopping;
    private readonly bool enhanceMicrophone;
    public bool Automatic { get; }
    public AudioMixer Mixer { get; private set; } = null!;
    public MixerInput[] Inputs => Mixer.CurrentInputs;
    public Task Completion { get; private set; } = Task.CompletedTask;
    public string OutputPath { get; }
    public string PartialPath { get; }
    public Exception? CaptureError => sources.Values.Select(s => s.Error).FirstOrDefault(e => e != null);
    public string? Warning { get; private set; }

    public RecordingSession(IEnumerable<DeviceChoice> choices, string folder, int bitrate, float systemGain, float micGain, bool automatic = false, bool enhanceMicrophone = true)
    {
        Automatic = automatic;
        this.enhanceMicrophone = enhanceMicrophone;
        Directory.CreateDirectory(folder);
        string name = $"Kayit_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{Guid.NewGuid().ToString("N")[..6]}";
        OutputPath = Path.Combine(folder, name + ".m4a");
        PartialPath = Path.Combine(folder, name + ".partial.m4a");
        try
        {
            var rates = MediaFoundationEncoder.GetEncodeBitrates(NAudio.MediaFoundation.AudioSubtypes.MFAudioFormat_AAC, 48000, 2);
            if (!rates.Contains(bitrate)) throw new InvalidOperationException("Bu AAC kalitesi sistemde desteklenmiyor. Başka kalite seçin. Windows N için Media Feature Pack gerekebilir.");
            AddSources(choices);
            if (sources.Count == 0) throw new IOException(Warning ?? "Kaydedilebilen ses kaynağı bulunamadı.");
            Mixer = new AudioMixer(sources.Values.Select(s => s.Input)) { SystemGain = systemGain, MicrophoneGain = micGain };
            Completion = Task.Run(() => MediaFoundationEncoder.EncodeToAac(Mixer, PartialPath, bitrate));
        }
        catch { Dispose(); throw; }
    }

    private void AddSources(IEnumerable<DeviceChoice> choices)
    {
        var warnings = new List<string>();
        foreach (var choice in choices.DistinctBy(c => c.Id))
        {
            if (sources.ContainsKey(choice.Id)) continue;
            try { sources.Add(choice.Id, new SourceCapture(enumerator.GetDevice(choice.Id), choice, enhanceMicrophone)); }
            catch (Exception ex)
            {
                if (!Automatic) throw;
                warnings.Add($"Kaynak açılamadı: {choice.Name}: {ex.Message}");
            }
        }
        Warning = warnings.Count == 0 ? null : string.Join("; ", warnings);
    }

    // Called serially by the UI, never concurrently with StopAsync or Dispose.
    public void SyncSources(IEnumerable<DeviceChoice> choices)
    {
        if (!Automatic || stopping) return;
        var desired = choices.DistinctBy(c => c.Id).ToArray();
        var wanted = desired.Select(c => c.Id).ToHashSet();
        AddSources(desired);
        var removed = sources.Where(p => !wanted.Contains(p.Key)).ToArray();
        foreach (var pair in removed) sources.Remove(pair.Key);
        Mixer.SetInputs(sources.Values.Select(s => s.Input));
        foreach (var pair in removed) pair.Value.Dispose();
        // Disconnected endpoints have now been removed. Remaining errors are real capture failures.
    }

    public async Task StopAsync()
    {
        stopping = true;
        Exception? stopError = null;
        foreach (var source in sources.Values)
            try { source.Stop(); } catch (Exception ex) { stopError ??= ex; }
        int drainMs = (int)Math.Ceiling(sources.Values.Select(s => s.Buffer.BufferedDuration.TotalMilliseconds).DefaultIfEmpty().Max()) + 100;
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
        foreach (var source in sources.Values) source.Dispose();
        sources.Clear();
        enumerator.Dispose();
        if (Completion.IsCompleted) Mixer?.Dispose();
    }

    private sealed class SourceCapture : IDisposable
    {
        private readonly MMDevice device;
        private WasapiCapture? capture;
        private volatile bool stopped;
        private Exception? error;
        public Exception? Error => Volatile.Read(ref error);
        public BufferedWaveProvider Buffer { get; } = null!;
        public MixerInput Input { get; } = null!;

        public SourceCapture(MMDevice device, DeviceChoice choice, bool enhanceMicrophone)
        {
            this.device = device;
            try
            {
                capture = choice.Microphone ? new WasapiCapture(device) : new WasapiLoopbackCapture(device);
                Buffer = new BufferedWaveProvider(capture.WaveFormat)
                {
                    BufferDuration = TimeSpan.FromSeconds(3), ReadFully = true, DiscardOnBufferOverflow = false
                };
                capture.DataAvailable += (_, e) =>
                {
                    try { Buffer.AddSamples(e.Buffer, 0, e.BytesRecorded); }
                    catch (Exception ex) { Interlocked.CompareExchange(ref error,
                        new IOException($"Ses tamponu işlenemedi: {choice.Name}", ex), null); }
                };
                capture.RecordingStopped += (_, e) =>
                {
                    if (!stopped) Interlocked.CompareExchange(ref error,
                        e.Exception ?? new IOException($"Ses kaynağı durdu: {choice.Name}"), null);
                };
                ISampleProvider samples = new StereoSource(Buffer.ToSampleProvider());
                if (samples.WaveFormat.SampleRate != AudioMixer.Rate)
                    samples = new WdlResamplingSampleProvider(samples, AudioMixer.Rate);
                if (choice.Microphone && enhanceMicrophone) samples = new MicrophoneEnhancer(samples);
                Input = new MixerInput(samples, choice.Microphone);
                capture.StartRecording();
            }
            catch { Dispose(); throw; }
        }
        public void Stop() { stopped = true; capture?.StopRecording(); }
        public void Dispose()
        {
            stopped = true;
            try { capture?.Dispose(); } catch { /* Release other endpoints too. */ }
            device.Dispose();
        }
    }
}
