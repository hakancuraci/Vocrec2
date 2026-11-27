using System.Collections.Concurrent;

namespace SesKayit.Core;

public sealed class EndpointChanges
{
    private readonly ConcurrentDictionary<string, long> revisions = new(StringComparer.OrdinalIgnoreCase);
    private long renderRevision, captureRevision;
    public void DeviceChanged(string id) => revisions.AddOrUpdate(id, 1, (_, previous) => previous + 1);
    public void DefaultChanged(bool microphone)
    {
        if (microphone) Interlocked.Increment(ref captureRevision); else Interlocked.Increment(ref renderRevision);
    }
    public long Revision(string id, bool microphone) => revisions.GetValueOrDefault(id) +
        (microphone ? Interlocked.Read(ref captureRevision) : Interlocked.Read(ref renderRevision));
}

/// <summary>Detects a stalled loopback only when Windows independently reports actual playback.</summary>
public sealed class LoopbackHealth
{
    private readonly TimeProvider clock;
    private readonly long opened;
    private long lastSignal;
    private long lastEvidence;
    private int missedPlayback;
    public LoopbackHealth(TimeProvider? clock = null)
    {
        this.clock = clock ?? TimeProvider.System; opened = this.clock.GetTimestamp(); lastSignal = opened;
    }
    public bool CanRefresh => clock.GetElapsedTime(opened) >= TimeSpan.FromSeconds(1);
    public void ObserveSamples(float[] buffer, int offset, int count)
    {
        for (int i = offset; i < offset + count; i++)
            if (float.IsFinite(buffer[i]) && Math.Abs(buffer[i]) >= 1f / 32767)
            { Interlocked.Exchange(ref lastSignal, clock.GetTimestamp()); return; }
    }
    public bool PlaybackWithoutCapture(float playbackPeak)
    {
        long now = clock.GetTimestamp();
        if (clock.GetElapsedTime(opened, now) < TimeSpan.FromSeconds(3) ||
            clock.GetElapsedTime(Interlocked.Read(ref lastSignal), now) < TimeSpan.FromSeconds(3))
        { missedPlayback = 0; return false; }
        if (clock.GetElapsedTime(lastEvidence, now) > TimeSpan.FromSeconds(8)) missedPlayback = 0;
        if (playbackPeak > 0.001f && (missedPlayback == 0 || clock.GetElapsedTime(lastEvidence, now) >= TimeSpan.FromMilliseconds(500)))
        { missedPlayback++; lastEvidence = now; }
        return missedPlayback >= 3;
    }
}

public sealed class SignalProbe(NAudio.Wave.ISampleProvider source, LoopbackHealth health) : NAudio.Wave.ISampleProvider
{
    public NAudio.Wave.WaveFormat WaveFormat => source.WaveFormat;
    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count); health.ObserveSamples(buffer, offset, read); return read;
    }
}
