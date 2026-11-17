using NAudio.Wave;
using SesKayit.Core;
using Xunit;

public class MixerTests
{
    private sealed class Constant(float value, int channels = 2, int rate = 48000) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(rate, channels);
        public int Read(float[] buffer, int offset, int count) { Array.Fill(buffer, value, offset, count); return count; }
    }
    private sealed class FiniteSource(float[] values, int channels) : ISampleProvider
    {
        private int position;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, channels);
        public int Read(float[] buffer, int offset, int count)
        {
            int read = Math.Min(count, values.Length - position);
            Array.Copy(values, position, buffer, offset, read); position += read; return read;
        }
    }

    [Fact]
    public void MixesBothSourcesWithIndependentGainsAndPreservesOffset()
    {
        using var mixer = new AudioMixer([new(new Constant(0.4f), false), new(new Constant(0.2f), true)]) { SystemGain = 0.5f, MicrophoneGain = 1 };
        byte[] pcm = Enumerable.Repeat((byte)0xCC, 200).ToArray();
        Assert.Equal(192, mixer.Read(pcm, 4, 192));
        Assert.InRange(BitConverter.ToInt16(pcm, 4), 13105, 13108);
        Assert.Equal(0xCC, pcm[0]); Assert.Equal(0xCC, pcm[199]);
        Assert.Equal(TimeSpan.FromMilliseconds(1), mixer.Duration);
    }

    [Theory]
    [InlineData(1f)] [InlineData(-1f)]
    public void LimitsSummedSignalInsteadOfWrappingPcm(float value)
    {
        using var mixer = new AudioMixer([new(new Constant(value), false), new(new Constant(value), true)]) { SystemGain = 1, MicrophoneGain = 1 };
        byte[] pcm = new byte[192]; mixer.Read(pcm, 0, pcm.Length);
        Assert.InRange(Math.Abs((int)BitConverter.ToInt16(pcm)), 32110, 32113);
        Assert.True(mixer.LimitedSamples > 0);
    }

    [Fact]
    public void NonFiniteInputDoesNotPoisonOtherSources()
    {
        using var mixer = new AudioMixer([new(new Constant(float.NaN), true), new(new Constant(0.5f), false)]) { SystemGain = 1 };
        byte[] pcm = new byte[192]; mixer.Read(pcm, 0, pcm.Length);
        Assert.InRange(BitConverter.ToInt16(pcm), 16382, 16385);
    }

    [Fact]
    public async Task FinishInterruptsBlockedEncoderRead()
    {
        using var mixer = new AudioMixer([new(new Constant(0), false)]);
        var read = Task.Run(() => mixer.Read(new byte[48000 * 4 * 60], 0, 48000 * 4 * 60));
        await Task.Delay(35); mixer.Finish();
        int result = await read.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(result < 48000 * 4 * 60);
        Assert.Equal(0, mixer.Read(new byte[4], 0, 4));
    }

    [Fact]
    public void FiniteInputBecomesSilenceInsteadOfReplayingOldSamples()
    {
        using var mixer = new AudioMixer([new(new FiniteSource([0.5f, 0.5f], 2), false)]) { SystemGain = 1 };
        byte[] pcm = new byte[192]; mixer.Read(pcm, 0, pcm.Length);
        Assert.NotEqual(0, BitConverter.ToInt16(pcm));
        Assert.All(pcm.Skip(4), b => Assert.Equal(0, b));
    }

    [Fact]
    public void MonoIsDuplicatedToBothStereoChannels()
    {
        var stereo = new StereoSource(new FiniteSource([0.2f, -0.4f], 1));
        float[] output = new float[6];
        Assert.Equal(4, stereo.Read(output, 1, 4));
        Assert.Equal(new float[] { 0, 0.2f, 0.2f, -0.4f, -0.4f, 0 }, output);
    }

    [Fact]
    public void SurroundSpeakersContributeToBothChannels()
    {
        var stereo = new StereoSource(new FiniteSource([0, 0, 0.6f, 0.6f, 0.6f, 0.6f], 6));
        float[] output = new float[2];
        Assert.Equal(2, stereo.Read(output, 0, 2));
        Assert.InRange(output[0], 0.1999f, 0.2001f); Assert.Equal(output[0], output[1]);
    }

    [Fact]
    public void RejectsUnconvertedSourcesAndEmptySelection()
    {
        Assert.Throws<ArgumentException>(() => new AudioMixer([]));
        Assert.Throws<ArgumentException>(() => new AudioMixer([new(new Constant(0, 1), false)]));
        Assert.Throws<ArgumentException>(() => new AudioMixer([new(new Constant(0, 2, 44100), false)]));
    }
}
