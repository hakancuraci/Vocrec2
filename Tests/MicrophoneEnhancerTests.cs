using NAudio.Wave;
using SesKayit.Core;
using Xunit;

namespace SesKayit.Tests;

public class MicrophoneEnhancerTests
{
    private sealed class Tone(double frequency, float amplitude) : ISampleProvider
    {
        private int frame;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public int Read(float[] buffer, int offset, int count)
        {
            for (int n = 0; n < count; n += 2, frame++)
                buffer[offset + n] = buffer[offset + n + 1] = amplitude * (float)Math.Sin(2 * Math.PI * frequency * frame / 48000);
            return count;
        }
    }
    private static double Rms(double hz, float amplitude)
    {
        var provider = new MicrophoneEnhancer(new Tone(hz, amplitude));
        var buffer = new float[96000];
        provider.Read(buffer, 0, buffer.Length); // settle gain and filter
        provider.Read(buffer, 0, buffer.Length);
        return Math.Sqrt(buffer.Select(v => (double)v * v).Average());
    }
    [Fact] public void AttenuatesRumbleRelativeToSpeech() => Assert.True(Rms(1000, .1f) > 8 * Rms(25, .1f));
    [Fact] public void ReducesQuietBackground() => Assert.True(Rms(1000, .002f) < .0005);
    [Fact] public void RaisesQuietSpeech() => Assert.True(Rms(1000, .05f) > .05 / Math.Sqrt(2) * 1.5);
    [Fact]
    public void PreservesStereoAndBoundsPeaks()
    {
        var provider = new MicrophoneEnhancer(new Tone(1000, 2));
        var buffer = new float[96000]; provider.Read(buffer, 0, buffer.Length);
        Assert.All(buffer, value => Assert.InRange(value, -.9f, .9f));
        for (int n = 0; n < buffer.Length; n += 2) Assert.Equal(buffer[n], buffer[n + 1]);
    }
}
