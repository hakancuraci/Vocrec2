using System.Diagnostics;
using NAudio.Wave;

namespace SesKayit.Core;

public sealed class MixerInput(ISampleProvider source, bool microphone)
{
    public ISampleProvider Source { get; } = source;
    public bool Microphone { get; } = microphone;
    public float Peak;
}

/// <summary>Clocked, bounded-memory PCM mixer. The encoder pulls from this provider.</summary>
public sealed class AudioMixer : IWaveProvider, IDisposable
{
    public const int Rate = 48000;
    private readonly MixerInput[] inputs;
    private readonly ManualResetEventSlim ended = new(false);
    private readonly Stopwatch clock = new();
    private readonly float[] sum = new float[1920];
    private readonly float[] scratch = new float[1920];
    private long frames;
    public WaveFormat WaveFormat { get; } = new(Rate, 16, 2);
    public float SystemGain = 0.7f;
    public float MicrophoneGain = 0.7f;
    public float OutputPeak;
    public long LimitedSamples;
    public TimeSpan Duration => TimeSpan.FromSeconds(Interlocked.Read(ref frames) / (double)Rate);

    public AudioMixer(IEnumerable<MixerInput> inputs)
    {
        this.inputs = inputs.ToArray();
        if (this.inputs.Length == 0) throw new ArgumentException("En az bir ses kaynağı seçin.");
        if (this.inputs.Any(i => i.Source.WaveFormat.SampleRate != Rate || i.Source.WaveFormat.Channels != 2))
            throw new ArgumentException("Kaynaklar 48 kHz stereo olmalıdır.");
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        if (count % 4 != 0) throw new ArgumentException("PCM kare boyutu 4 bayttır.");
        if (!clock.IsRunning) clock.Start();
        int written = 0;
        while (written < count && !ended.IsSet)
        {
            int samples = Math.Min(sum.Length, (count - written) / 2);
            double due = (frames + samples / 2d) / Rate;
            int delay = (int)Math.Ceiling((due - clock.Elapsed.TotalSeconds) * 1000);
            if (delay > 0 && ended.Wait(delay)) break;
            Mix(samples);
            for (int n = 0; n < samples; n++)
            {
                short value = (short)Math.Round(Math.Clamp(sum[n], -1f, 1f) * 32767);
                buffer[offset + written++] = (byte)value;
                buffer[offset + written++] = (byte)(value >> 8);
            }
            Interlocked.Add(ref frames, samples / 2);
        }
        return written;
    }

    private void Mix(int samples)
    {
        Array.Clear(sum, 0, samples);
        foreach (var input in inputs)
        {
            int read = input.Source.Read(scratch, 0, samples);
            float gain = input.Microphone ? Volatile.Read(ref MicrophoneGain) : Volatile.Read(ref SystemGain);
            float peak = 0;
            for (int n = 0; n < read; n++)
            {
                float sample = float.IsFinite(scratch[n]) ? scratch[n] * gain : 0;
                peak = Math.Max(peak, Math.Abs(sample));
                sum[n] += sample;
            }
            Volatile.Write(ref input.Peak, peak);
        }
        float outputPeak = 0;
        for (int n = 0; n < samples; n++)
        {
            if (Math.Abs(sum[n]) > 0.98f) Interlocked.Increment(ref LimitedSamples);
            // Hard safety ceiling; use the meters/gains to avoid sustained limiting.
            sum[n] = Math.Clamp(sum[n], -0.98f, 0.98f);
            outputPeak = Math.Max(outputPeak, Math.Abs(sum[n]));
        }
        Volatile.Write(ref OutputPeak, outputPeak);
    }

    public void Finish() => ended.Set();
    public void Dispose() => ended.Dispose();
}

/// <summary>Downmix native mono/stereo/surround endpoint layouts to stereo.</summary>
public sealed class StereoSource(ISampleProvider source) : ISampleProvider
{
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
    private float[] buffer = [];
    public int Read(float[] output, int offset, int count)
    {
        int channels = source.WaveFormat.Channels;
        int required = count / 2 * channels;
        if (buffer.Length < required) buffer = new float[required];
        int read = source.Read(buffer, 0, required);
        int frames = read / channels;
        for (int f = 0; f < frames; f++)
        {
            int p = f * channels;
            float left = buffer[p], right = channels == 1 ? left : buffer[p + 1];
            // Front L/R remain stereo; remaining speakers contribute equally at reduced gain.
            if (channels > 2)
            {
                float rest = 0;
                for (int c = 2; c < channels; c++) rest += buffer[p + c];
                rest *= 0.5f / (channels - 2);
                left = (left + rest) / 1.5f;
                right = (right + rest) / 1.5f;
            }
            output[offset + f * 2] = left;
            output[offset + f * 2 + 1] = right;
        }
        return frames * 2;
    }
}
