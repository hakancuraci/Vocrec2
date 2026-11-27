using NAudio.Wave;
using SesKayit.Core;
using Xunit;

namespace SesKayit.Tests;

public class EndpointRecoveryTests
{
    private sealed class Clock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        public void Advance(double seconds) => ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }

    [Fact]
    public void SameIdRouteChangesAndDefaultsHaveIndependentMonotonicRevisions()
    {
        var changes = new EndpointChanges();
        changes.DeviceChanged("realtek"); long first = changes.Revision("realtek", false);
        changes.DeviceChanged("realtek"); Assert.True(changes.Revision("realtek", false) > first);
        long mic = changes.Revision("mic", true);
        changes.DefaultChanged(false); Assert.Equal(mic, changes.Revision("mic", true));
        Assert.True(changes.Revision("other-render", false) > 0);
        changes.DefaultChanged(true); Assert.True(changes.Revision("mic", true) > mic);
    }

    [Fact]
    public void GenuineSilenceDoesNotTriggerRepeatedCaptureRestarts()
    {
        var clock = new Clock(); var health = new LoopbackHealth(clock);
        for (int i = 0; i < 120; i++) { clock.Advance(1); Assert.False(health.PlaybackWithoutCapture(0)); }
    }

    [Fact]
    public void IndependentPlaybackEvidenceDetectsSilentLoopbackWithoutCountingRepeatedUiRefreshes()
    {
        var clock = new Clock(); var health = new LoopbackHealth(clock);
        clock.Advance(3);
        Assert.False(health.PlaybackWithoutCapture(0.3f));
        for (int i = 0; i < 10; i++) Assert.False(health.PlaybackWithoutCapture(0.3f));
        clock.Advance(1); Assert.False(health.PlaybackWithoutCapture(0.3f));
        clock.Advance(1); Assert.True(health.PlaybackWithoutCapture(0.3f));
        var reopened = new LoopbackHealth(clock);
        Assert.False(reopened.PlaybackWithoutCapture(0.3f)); Assert.False(reopened.CanRefresh);
    }

    [Fact]
    public void CapturedSignalAndStalePlaybackEvidencePreventFalseReconnection()
    {
        var clock = new Clock(); var health = new LoopbackHealth(clock);
        for (int i = 0; i < 12; i++)
        {
            clock.Advance(1); health.ObserveSamples([0.01f], 0, 1); Assert.False(health.PlaybackWithoutCapture(0.4f));
        }
        clock.Advance(4); Assert.False(health.PlaybackWithoutCapture(0.4f));
        clock.Advance(9); Assert.False(health.PlaybackWithoutCapture(0.4f));
    }

    [Fact]
    public void SourceProbeSeesRealSamplesBeforeAZeroMixerGainAndTheOutputTracksCompleteSilence()
    {
        var clock = new Clock(); var health = new LoopbackHealth(clock);
        using var mixer = new AudioMixer([new(new SignalProbe(new Constant(), health), false)]) { SystemGain = 0 };
        byte[] bytes = new byte[192];
        for (int i = 0; i < 10; i++)
        {
            clock.Advance(1); mixer.Read(bytes, 0, bytes.Length); Assert.False(health.PlaybackWithoutCapture(0.4f));
        }
        Assert.Equal(0, mixer.NonSilentSamples);
        mixer.SystemGain = 1; mixer.Read(bytes, 0, bytes.Length); Assert.True(mixer.NonSilentSamples > 0);
    }

    private sealed class Constant : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public int Read(float[] samples, int offset, int count) { Array.Fill(samples, 0.25f, offset, count); return count; }
    }
}
