using NAudio.MediaFoundation;
using NAudio.Wave;
using SesKayit.Core;
using Xunit;

namespace SesKayit.WindowsTests;

public sealed class AacRoundTripTests
{
    // Hardware-free integration test of the actual Windows Media Foundation codec.
    // These tests must fail, rather than silently skip, if AAC is unavailable on CI.
    [Theory]
    [InlineData(128000)]
    [InlineData(160000)]
    [InlineData(192000)]
    public void MixedSourcesSurviveAacRoundTrip(int bitrate)
    {
        Assert.True(OperatingSystem.IsWindows(), "Bu test Windows Media Foundation gerektirir.");
        const int seconds = 2;
        string? artifactDirectory = Environment.GetEnvironmentVariable("SESKAYIT_TEST_AUDIO_DIR");
        string directory = artifactDirectory ?? Path.Combine(Path.GetTempPath(), "SesKayit-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"İki kaynak denemesi-{bitrate}.m4a");

        try
        {
            Assert.Contains(bitrate, MediaFoundationEncoder.GetEncodeBitrates(AudioSubtypes.MFAudioFormat_AAC, AudioMixer.Rate, 2));
            using var mixer = new AudioMixer([
                new(new ToneSource(440, 0.25f), false),
                new(new ToneSource(880, 0.20f), true)
            ]) { SystemGain = 1, MicrophoneGain = 1 };
            var finite = new LimitedProvider(mixer, seconds * AudioMixer.Rate * mixer.WaveFormat.BlockAlign);
            MediaFoundationEncoder.EncodeToAac(finite, path, bitrate);

            Assert.Equal(0, finite.Remaining);
            Assert.Equal(TimeSpan.FromSeconds(seconds), mixer.Duration);
            long expectedBytes = bitrate / 8 * seconds;
            Assert.InRange(new FileInfo(path).Length, expectedBytes / 2, expectedBytes * 2);

            // Reopening after EncodeToAac returns also checks container finalization.
            using var reader = new MediaFoundationReader(path);
            Assert.Equal(AudioMixer.Rate, reader.WaveFormat.SampleRate);
            Assert.Equal(2, reader.WaveFormat.Channels);
            var decoded = reader.ToSampleProvider();
            var samples = new List<float>();
            var block = new float[4096];
            int read;
            while ((read = decoded.Read(block, 0, block.Length)) > 0)
            {
                samples.AddRange(block.Take(read));
                Assert.True(samples.Count < AudioMixer.Rate * 2 * 4, "Çözülen ses beklenen süreden uzun.");
            }
            double duration = samples.Count / (2d * AudioMixer.Rate);
            Assert.InRange(duration, seconds - 0.15, seconds + 0.25);
            Assert.All(samples, sample => Assert.True(float.IsFinite(sample)));
            // Analyze an interior one-second window to avoid AAC priming/padding.
            // Both independent inputs must be audible on both output channels.
            for (int channel = 0; channel < 2; channel++)
            {
                Assert.InRange(ToneAmplitude(samples, channel, 440), 0.17, 0.32);
                Assert.InRange(ToneAmplitude(samples, channel, 880), 0.13, 0.27);
                Assert.InRange(ToneAmplitude(samples, channel, 3000), 0, 0.02);
            }
        }
        finally
        {
            // Keep the tiny synthetic files for download from Actions, including failures.
            if (artifactDirectory is null) Directory.Delete(directory, recursive: true);
        }
    }

    private static double ToneAmplitude(List<float> samples, int channel, int frequency)
    {
        const int startFrame = AudioMixer.Rate / 2;
        const int count = AudioMixer.Rate;
        Assert.True(samples.Count >= (startFrame + count) * 2);
        double sin = 0, cos = 0;
        for (int n = 0; n < count; n++)
        {
            double phase = 2 * Math.PI * frequency * n / AudioMixer.Rate;
            float value = samples[(startFrame + n) * 2 + channel];
            sin += value * Math.Sin(phase);
            cos += value * Math.Cos(phase);
        }
        return 2 * Math.Sqrt(sin * sin + cos * cos) / count;
    }

    private sealed class ToneSource(int frequency, float amplitude) : ISampleProvider
    {
        private long frame;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(AudioMixer.Rate, 2);
        public int Read(float[] buffer, int offset, int count)
        {
            for (int n = 0; n < count; n += 2, frame++)
            {
                float value = amplitude * (float)Math.Sin(2 * Math.PI * frequency * frame / AudioMixer.Rate);
                buffer[offset + n] = buffer[offset + n + 1] = value;
            }
            return count;
        }
    }

    private sealed class LimitedProvider(IWaveProvider source, long length) : IWaveProvider
    {
        public long Remaining { get; private set; } = length;
        public WaveFormat WaveFormat => source.WaveFormat;
        public int Read(byte[] buffer, int offset, int count)
        {
            if (Remaining == 0) return 0;
            int read = source.Read(buffer, offset, (int)Math.Min(Remaining, count));
            Remaining -= read;
            return read;
        }
    }
}
