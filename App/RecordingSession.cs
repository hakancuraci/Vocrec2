using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SesKayit.Core;

namespace SesKayit;

public sealed record DeviceChoice(string Id, string Name, bool Microphone, float PlaybackPeak = 0, string FormatSignature = "")
{
    public override string ToString() => Name;
}

public sealed class RecordingSession : IRecordingSession
{
    private sealed record OpenedSource(IRecordingSource Source, DeviceChoice Choice, long Revision);
    private readonly Dictionary<string, OpenedSource> sources = [];
    private readonly MMDeviceEnumerator? enumerator;
    private readonly AudioEndpointNotifications? notifications;
    private readonly EndpointChanges changes;
    private readonly Func<DeviceChoice, IRecordingSource> createSource;
    private bool notificationsRegistered;
    private bool stopping;
    private bool disposed;
    private readonly bool enhanceMicrophone;
    public bool Automatic { get; set; }
    public AudioMixer Mixer { get; private set; } = null!;
    public MixerInput[] Inputs => Mixer.CurrentInputs;
    public Task Completion { get; private set; } = Task.CompletedTask;
    public string OutputPath { get; }
    public string PartialPath { get; }
    public Exception? CaptureError => sources.Values.Select(s => s.Source.Error).FirstOrDefault(e => e != null);
    public string? Warning { get; private set; }

    public RecordingSession(IEnumerable<DeviceChoice> choices, string folder, int bitrate, float systemGain, float micGain, bool automatic = false, bool enhanceMicrophone = true)
        : this(choices, folder, bitrate, systemGain, micGain, automatic, enhanceMicrophone, null, null, null)
    { }

    internal RecordingSession(IEnumerable<DeviceChoice> choices, string folder, int bitrate, float systemGain, float micGain,
        bool automatic, bool enhanceMicrophone, Func<DeviceChoice, IRecordingSource>? sourceFactory,
        Func<AudioMixer, string, int, Task>? encoder, EndpointChanges? changes)
    {
        Automatic = automatic;
        this.enhanceMicrophone = enhanceMicrophone;
        this.changes = changes ?? new EndpointChanges();
        if (sourceFactory == null)
        {
            enumerator = new MMDeviceEnumerator(); notifications = new AudioEndpointNotifications(this.changes);
            createSource = choice => new SourceCapture(enumerator.GetDevice(choice.Id), choice, this.enhanceMicrophone);
        }
        else createSource = sourceFactory;
        Directory.CreateDirectory(folder);
        var startedAt = DateTime.Now;
        string name = RecordingDates.WithPrefix($"Kayit_{startedAt:HH-mm-ss}_{Guid.NewGuid().ToString("N")[..6]}", DateOnly.FromDateTime(startedAt));
        OutputPath = Path.Combine(folder, name + ".m4a");
        PartialPath = Path.Combine(folder, name + ".partial.m4a");
        try
        {
            if (encoder == null)
            {
                var rates = MediaFoundationEncoder.GetEncodeBitrates(NAudio.MediaFoundation.AudioSubtypes.MFAudioFormat_AAC, 48000, 2);
                if (!rates.Contains(bitrate)) throw new InvalidOperationException("Bu AAC kalitesi sistemde desteklenmiyor. Başka kalite seçin. Windows N için Media Feature Pack gerekebilir.");
            }
            if (notifications != null)
                try { notificationsRegistered = enumerator!.RegisterEndpointNotificationCallback(notifications) == 0; }
                catch { /* Enumeration, format comparison and playback health remain available. */ }
            AddSources(choices);
            if (sources.Count == 0) throw new IOException(Warning ?? "Kaydedilebilen ses kaynağı bulunamadı.");
            Mixer = new AudioMixer(sources.Values.Select(s => s.Source.Input)) { SystemGain = systemGain, MicrophoneGain = micGain };
            Completion = encoder != null ? encoder(Mixer, PartialPath, bitrate) : Task.Run(() => MediaFoundationEncoder.EncodeToAac(Mixer, PartialPath, bitrate));
        }
        catch { Dispose(); throw; }
    }

    private void AddSources(IEnumerable<DeviceChoice> choices, bool tolerateFailures = false)
    {
        var warnings = new List<string>();
        foreach (var choice in choices.DistinctBy(c => c.Id))
        {
            if (sources.ContainsKey(choice.Id)) continue;
            try
            {
                long revision = changes.Revision(choice.Id, choice.Microphone);
                sources.Add(choice.Id, new OpenedSource(createSource(choice), choice, revision));
            }
            catch (Exception ex)
            {
                if (!Automatic && !tolerateFailures) throw;
                warnings.Add($"Kaynak açılamadı: {choice.Name}: {ex.Message}");
            }
        }
        Warning = warnings.Count == 0 ? null : string.Join("; ", warnings);
    }

    // Called serially by the UI, never concurrently with StopAsync or Dispose.
    public void SyncSources(IEnumerable<DeviceChoice> choices)
    {
        if (stopping) return;
        var desired = choices.DistinctBy(c => c.Id).ToArray();
        var wanted = desired.Select(c => c.Id).ToHashSet();
        var byId = desired.ToDictionary(c => c.Id);
        bool Restart(OpenedSource opened)
        {
            var choice = byId[opened.Choice.Id];
            bool changed = opened.Revision != changes.Revision(choice.Id, choice.Microphone) ||
                (choice.FormatSignature.Length > 0 && opened.Choice.FormatSignature.Length > 0 && choice.FormatSignature != opened.Choice.FormatSignature);
            return (changed && opened.Source.Health.CanRefresh) || (!choice.Microphone && opened.Source.Health.PlaybackWithoutCapture(choice.PlaybackPeak));
        }
        var removed = sources.Where(p => !wanted.Contains(p.Key) || p.Value.Source.Error != null || Restart(p.Value)).ToArray();
        foreach (var pair in removed) sources.Remove(pair.Key);
        Mixer.SetInputs(sources.Values.Select(s => s.Source.Input));
        foreach (var pair in removed) pair.Value.Source.Dispose();
        // Reopen failed devices only after their previous capture is detached and disposed.
        // A failing new device must not interrupt the remaining sources or the encoder.
        AddSources(desired, tolerateFailures: true);
        Mixer.SetInputs(sources.Values.Select(s => s.Source.Input));
        if (removed.Any(p => wanted.Contains(p.Key))) Warning = string.Join("; ", new[] { Warning, "Ses yönlendirmesi yenilendi: " + string.Join(", ", removed.Where(p => wanted.Contains(p.Key)).Select(p => p.Value.Choice.Name)) }.Where(s => !string.IsNullOrEmpty(s)));
    }

    public async Task StopAsync()
    {
        stopping = true;
        Exception? stopError = null;
        foreach (var source in sources.Values)
            try { source.Source.Stop(); } catch (Exception ex) { stopError ??= ex; }
        int drainMs = (int)Math.Ceiling(sources.Values.Select(s => s.Source.BufferedDuration.TotalMilliseconds).DefaultIfEmpty().Max()) + 100;
        await Task.WhenAny(Completion, Task.Delay(drainMs));
        Mixer.Finish();
        await Completion;
        File.Move(PartialPath, OutputPath);
        if (CaptureError != null || stopError != null)
            throw new IOException($"Kayıt erken durdu. Tamamlanan bölüm kaydedildi:\n{OutputPath}", CaptureError ?? stopError);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        stopping = true;
        Mixer?.Finish();
        Mixer?.SetInputs([]);
        foreach (var source in sources.Values) source.Source.Dispose();
        sources.Clear();
        if (notificationsRegistered) { try { enumerator!.UnregisterEndpointNotificationCallback(notifications!); } catch { } notificationsRegistered = false; }
        enumerator?.Dispose();
        if (Completion.IsCompleted) Mixer?.Dispose();
        else _ = Completion.ContinueWith(completed => { _ = completed.Exception; Mixer?.Dispose(); }, TaskScheduler.Default);
    }

    private sealed class SourceCapture : IRecordingSource
    {
        private readonly MMDevice device;
        private WasapiCapture? capture;
        private volatile bool stopped;
        private Exception? error;
        public Exception? Error => Volatile.Read(ref error);
        public BufferedWaveProvider Buffer { get; } = null!;
        public MixerInput Input { get; } = null!;
        public LoopbackHealth Health { get; } = new();
        public TimeSpan BufferedDuration => Buffer.BufferedDuration;

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
                samples = new SignalProbe(samples, Health);
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

internal interface IRecordingSource : IDisposable
{
    MixerInput Input { get; }
    Exception? Error { get; }
    LoopbackHealth Health { get; }
    TimeSpan BufferedDuration { get; }
    void Stop();
}
