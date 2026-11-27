using NAudio.CoreAudioApi;
using NAudio.Wave;
using SesKayit.Core;
using Xunit;

namespace SesKayit.WindowsTests;

public class EndpointRecoveryTests
{
    private sealed class Clock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref ticks);
        public void Advance(double seconds) => Interlocked.Add(ref ticks, TimeSpan.FromSeconds(seconds).Ticks);
    }

    private sealed class Source : IRecordingSource, ISampleProvider
    {
        private readonly float value;
        public bool Disposed;
        public Source(bool microphone, Clock clock, float value)
        { this.value = value; Health = new(clock); Input = new(new SignalProbe(this, Health), microphone); }
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public LoopbackHealth Health { get; }
        public MixerInput Input { get; }
        public Exception? Error => null;
        public TimeSpan BufferedDuration => TimeSpan.Zero;
        public int Read(float[] samples, int offset, int count)
        { ObjectDisposedException.ThrowIf(Disposed, this); Array.Fill(samples, value, offset, count); return count; }
        public void Stop() { }
        public void Dispose() => Disposed = true;
    }

    private static Task Encode(AudioMixer mixer, string path, int _) => Task.Run(() =>
    {
        using var output = File.Create(path); byte[] pcm = new byte[1920]; int count;
        while ((count = mixer.Read(pcm, 0, pcm.Length)) > 0) output.Write(pcm, 0, count);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameEndpointIdIsReopenedAfterJackNotificationWithoutRestartingTheEncoder(bool formatChanged)
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var clock = new Clock(); var changes = new EndpointChanges();
        var opened = new List<(DeviceChoice Choice, Source Source)>();
        var choices = new[] { new DeviceChoice("realtek", "Hoparlör / Kulaklık", false, FormatSignature: "stereo-48000"), new DeviceChoice("mic", "Mikrofon", true) };
        using var session = new RecordingSession(choices, root, 192000, 1, 1, false, false,
            choice => { var source = new Source(choice.Microphone, clock, 0.2f); opened.Add((choice, source)); return source; }, Encode, changes);
        try
        {
            string path = session.OutputPath; var encoder = session.Completion; var mixer = session.Mixer;
            clock.Advance(2);
            if (formatChanged) choices[0] = choices[0] with { FormatSignature = "stereo-44100" };
            else new AudioEndpointNotifications(changes).OnPropertyValueChanged("realtek", PropertyKeys.PKEY_AudioEndpoint_JackSubType);
            session.SyncSources(choices);
            Assert.Equal(3, opened.Count); Assert.True(opened[0].Source.Disposed); Assert.False(opened[1].Source.Disposed);
            Assert.Same(encoder, session.Completion); Assert.Same(mixer, session.Mixer); Assert.Equal(path, session.OutputPath);
            Assert.False(session.Completion.IsCompleted); Assert.Equal(2, session.Inputs.Length);
            clock.Advance(2); session.SyncSources(choices); Assert.Equal(3, opened.Count);
            await session.StopAsync(); Assert.True(File.Exists(path));
        }
        finally { session.Dispose(); await session.Completion; if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task StalledSilentOutputRecoversWhenWindowsReportsPlaybackButQuietOutputsAreLeftAlone()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var clock = new Clock(); int opens = 0;
        var choices = new[] { new DeviceChoice("realtek", "Kulaklık", false, PlaybackPeak: 0.4f) };
        using var session = new RecordingSession(choices, root, 192000, 1, 1, true, false,
            choice => new Source(false, clock, ++opens == 1 ? 0 : 0.25f), Encode, new());
        try
        {
            clock.Advance(3); session.SyncSources(choices); clock.Advance(1); session.SyncSources(choices);
            clock.Advance(1); session.SyncSources(choices); Assert.Equal(2, opens);
            for (int i = 0; i < 200 && Interlocked.Read(ref session.Mixer.NonSilentSamples) == 0; i++) await Task.Delay(10);
            Assert.True(session.Mixer.NonSilentSamples > 0);
            clock.Advance(5); session.SyncSources([choices[0] with { PlaybackPeak = 0 }]); Assert.Equal(2, opens);
            await session.StopAsync();
        }
        finally { session.Dispose(); await session.Completion; if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void WindowsEndpointCallbackRegistersAndDefaultMultimediaChangesInvalidateOnlyRender()
    {
        var changes = new EndpointChanges(); var callback = new AudioEndpointNotifications(changes);
        using var enumerator = new MMDeviceEnumerator();
        Assert.Equal(0, enumerator.RegisterEndpointNotificationCallback(callback));
        try
        {
            callback.OnDefaultDeviceChanged(DataFlow.Render, Role.Multimedia, "realtek");
            Assert.True(changes.Revision("realtek", false) > 0); Assert.Equal(0, changes.Revision("mic", true));
        }
        finally { Assert.Equal(0, enumerator.UnregisterEndpointNotificationCallback(callback)); }
    }
}
