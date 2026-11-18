using NAudio.Wave;
using NAudio.Dsp;

namespace SesKayit.Core;

/// <summary>Speech high-pass, linked stereo downward expander, bounded speech gain and limiter.</summary>
public sealed class MicrophoneEnhancer : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly BiQuadFilter[] highPass, lowPass;
    private double envelope, gain = 1, gateGain = 1;
    public WaveFormat WaveFormat => source.WaveFormat;
    public MicrophoneEnhancer(ISampleProvider source)
    {
        this.source = source;
        highPass = Enumerable.Range(0, WaveFormat.Channels).Select(_ => BiQuadFilter.HighPassFilter(WaveFormat.SampleRate, 90, 0.707f)).ToArray();
        lowPass = Enumerable.Range(0, WaveFormat.Channels).Select(_ => BiQuadFilter.LowPassFilter(WaveFormat.SampleRate, 10000, 0.707f)).ToArray();
    }
    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count), channels = WaveFormat.Channels;
        double attack = Math.Exp(-1d / (WaveFormat.SampleRate * .005)), release = Math.Exp(-1d / (WaveFormat.SampleRate * .15));
        for (int n = 0; n < read; n += channels)
        {
            double peak = 0;
            for (int c = 0; c < channels && n + c < read; c++)
            {
                int i = offset + n + c;
                buffer[i] = lowPass[c].Transform(highPass[c].Transform(float.IsFinite(buffer[i]) ? buffer[i] : 0));
                peak = Math.Max(peak, Math.Abs(buffer[i]));
            }
            double coeff = peak > envelope ? attack : release;
            envelope = coeff * envelope + (1 - coeff) * peak;
            // Do not amplify room noise. Expansion has a floor to preserve quiet syllables.
            double targetGate = Math.Clamp(envelope / .012, .12, 1);
            targetGate *= targetGate;
            gateGain += (targetGate - gateGain) * (targetGate > gateGain ? .02 : .0003);
            double targetGain = envelope > .018 ? Math.Clamp(.25 / envelope, 1, 3) : 1;
            gain += (targetGain - gain) * (targetGain < gain ? .01 : .00005);
            for (int c = 0; c < channels && n + c < read; c++)
            {
                int i = offset + n + c;
                buffer[i] = (float)Math.Clamp(buffer[i] * gateGain * gain, -.9, .9);
            }
        }
        return read;
    }
}
