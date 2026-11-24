using System.Text.Json;

namespace SesKayit.Core;

public enum ProcessingKind { Recording, TextSummary, AudioSummary }
public enum ProcessingStatus { Queued, Running, Completed, Failed }
public sealed record ProcessingEvent(DateTimeOffset At, string Message);

public sealed record ProcessingJob
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public long Sequence { get; init; }
    public string[] Sources { get; init; } = [];
    public ProcessingKind Kind { get; init; }
    public PostProcessingSettings Settings { get; init; } = new();
    public string Profile { get; init; } = "";
    public string Title { get; init; } = "";
    public bool UseRecordingName { get; init; }
    public DateOnly RecordedOn { get; init; }
    public ProcessingStatus Status { get; init; } = ProcessingStatus.Queued;
    public string Phase { get; init; } = "Kuyrukta";
    public DateTimeOffset EnqueuedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public string Error { get; init; } = "";
    public int Attempts { get; init; }
    public ProcessingEvent[] Events { get; init; } = [];
    public string[] Outputs { get; init; } = [];
    public string DisplayName => Title.Length > 0 ? Title : string.Join(" + ", Sources.Select(Path.GetFileNameWithoutExtension));
}

/// <summary>One durable FIFO shared by automatic recordings and manual summary requests.</summary>
public sealed class ProcessingQueue
{
    private readonly object sync = new();
    private readonly string folder;
    private readonly Func<ProcessingJob, Action<string>, Task> execute;
    private readonly Dictionary<string, ProcessingJob> jobs = [];
    private readonly HashSet<string> scheduled = [];
    private Task tail = Task.CompletedTask;
    private long sequence, revision;
    public string StorageWarning { get; private set; } = "";
    public Task Completion { get { lock (sync) return tail; } }
    public long Revision { get { lock (sync) return revision; } }

    public ProcessingQueue(string folder, Func<ProcessingJob, Action<string>, Task> execute)
    {
        this.folder = folder; this.execute = execute;
        if (!Directory.Exists(folder)) return;
        string[] files;
        try { files = Directory.GetFiles(folder, "*.json"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StorageWarning = "İşlem geçmişi okunamadı: " + ex.Message; return; }
        foreach (string file in files)
        {
            try
            {
                var job = JsonSerializer.Deserialize<ProcessingJob>(File.ReadAllText(file));
                if (job == null || !ValidId(job.Id) || job.Sources == null || job.Settings == null || !Enum.IsDefined(job.Status))
                { StorageWarning = "Geçersiz bir geçmiş kaydı atlandı: " + Path.GetFileName(file); continue; }
                job = job with { Events = job.Events ?? [], Outputs = job.Outputs ?? [], Title = job.Title ?? "", Profile = job.Profile ?? "", Error = job.Error ?? "", Phase = job.Phase ?? "Kuyrukta" };
                jobs[job.Id] = job; sequence = Math.Max(sequence, job.Sequence);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            { StorageWarning = "Bir geçmiş kaydı okunamadı: " + ex.Message; }
        }
    }

    public ProcessingJob[] Snapshot()
    {
        lock (sync) return jobs.Values.OrderByDescending(j => j.Sequence)
            .Select(j => j with { Sources = j.Sources.ToArray(), Events = j.Events.ToArray(), Outputs = j.Outputs.ToArray() }).ToArray();
    }

    public bool Enqueue(ProcessingJob job)
    {
        lock (sync)
        {
            if (!ValidId(job.Id)) throw new ArgumentException("Geçersiz işlem kimliği.");
            if (jobs.TryGetValue(job.Id, out var old) && old.Status != ProcessingStatus.Failed) return false;
            job = job with { Sources = job.Sources.ToArray(), Sequence = ++sequence, EnqueuedAt = DateTimeOffset.Now, Status = ProcessingStatus.Queued,
                Phase = "Kuyrukta", StartedAt = null, FinishedAt = null, Error = "",
                Attempts = old?.Attempts ?? job.Attempts, Outputs = old?.Outputs ?? job.Outputs,
                Events = (old?.Events ?? job.Events).Append(new(DateTimeOffset.Now, "Kuyruğa eklendi")).TakeLast(200).ToArray() };
            Save(job); jobs[job.Id] = job; revision++; Schedule(job.Id); return true;
        }
    }

    public void ResumeInterrupted()
    {
        lock (sync)
        {
            foreach (var job in jobs.Values.Where(j => j.Status is ProcessingStatus.Queued or ProcessingStatus.Running).OrderBy(j => j.Sequence).ToArray())
            {
                if (scheduled.Contains(job.Id)) continue;
                Update(job with { Status = ProcessingStatus.Queued, Phase = "Kuyrukta", StartedAt = null,
                    Events = job.Events.Append(new(DateTimeOffset.Now, "Uygulama açıldı; bekleyen iş sıraya alındı")).TakeLast(200).ToArray() });
                Schedule(job.Id);
            }
        }
    }

    public bool Retry(string id, PostProcessingSettings settings, string profile)
    {
        lock (sync)
        {
            return jobs.TryGetValue(id, out var job) && job.Status == ProcessingStatus.Failed &&
                Enqueue(job with { Settings = settings, Profile = profile });
        }
    }

    private void Schedule(string id)
    {
        scheduled.Add(id);
        var previous = tail;
        tail = Task.Run(async () => { await previous; await RunAsync(id); });
    }

    private async Task RunAsync(string id)
    {
        ProcessingJob job;
        lock (sync)
        {
            job = jobs[id] with { Status = ProcessingStatus.Running, Phase = "Başlıyor", StartedAt = DateTimeOffset.Now, Attempts = jobs[id].Attempts + 1 };
            Update(job);
        }
        void Report(string message)
        {
            lock (sync)
            {
                var current = jobs[id];
                string? output = new[] { "Metin kaydedildi: ", "Özet kaydedildi: " }
                    .Where(message.StartsWith).Select(prefix => message[prefix.Length..]).FirstOrDefault();
                Update(current with { Phase = Phase(message, current.Phase),
                    Events = current.Events.Append(new(DateTimeOffset.Now, message)).TakeLast(200).ToArray(),
                    Outputs = output == null ? current.Outputs : current.Outputs.Append(output).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() });
            }
        }
        try
        {
            Report($"İşlem başladı · Profil: {job.Profile} · Model: {job.Settings.LlmModel}");
            await execute(job, Report);
            lock (sync) Update(jobs[id] with { Status = ProcessingStatus.Completed, Phase = "Tamamlandı", FinishedAt = DateTimeOffset.Now,
                Events = jobs[id].Events.Append(new(DateTimeOffset.Now, "İşlem tamamlandı")).TakeLast(200).ToArray() });
        }
        catch (Exception ex)
        {
            lock (sync) Update(jobs[id] with { Status = ProcessingStatus.Failed, Phase = "Hata", Error = ex.Message, FinishedAt = DateTimeOffset.Now,
                Events = jobs[id].Events.Append(new(DateTimeOffset.Now, "Hata: " + ex.Message)).TakeLast(200).ToArray() });
        }
        finally { lock (sync) { if (jobs[id].Status != ProcessingStatus.Queued) scheduled.Remove(id); } }
    }

    private static string Phase(string message, string previous)
    {
        if (message.StartsWith("STT", StringComparison.Ordinal)) return "STT";
        if (message.StartsWith("LLM özeti", StringComparison.Ordinal)) return "LLM özeti";
        if (message.StartsWith("Özet için", StringComparison.Ordinal)) return "Dosya adı";
        if (message.StartsWith("Özet kaydedildi", StringComparison.Ordinal)) return "Özet kaydedildi";
        if (message.StartsWith("Metin kaydedildi", StringComparison.Ordinal)) return "Metin hazır";
        if (message.StartsWith("Metinler", StringComparison.Ordinal)) return "Metinler birleştiriliyor";
        return previous;
    }

    private void Update(ProcessingJob job)
    {
        jobs[job.Id] = job; revision++;
        try { Save(job); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { StorageWarning = "İşlem geçmişi diske kaydedilemedi: " + ex.Message; }
    }

    private void Save(ProcessingJob job)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, job.Id + ".json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(job)); File.Move(path + ".tmp", path, true);
    }

    private static bool ValidId(string id) => !string.IsNullOrEmpty(id) && id.Length <= 100 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
