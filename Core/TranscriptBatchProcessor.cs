using System.Text;

namespace SesKayit.Core;

public sealed class TranscriptBatchProcessor(HttpClient http, Func<string, CancellationToken, Task<string>> transcribe,
    Func<PostProcessingSettings, string>? resolveBearerToken = null)
{
    public async Task ProcessAsync(ProcessingJob job, Action<string> progress, CancellationToken token = default)
    {
        job.Settings.Validate();
        if (job.Settings.Endpoint() == null) throw new ArgumentException("Özet için LLM adresini, modelini ve promptunu girin.");
        if (job.Sources.Length == 0) throw new ArgumentException("En az bir kaynak seçin.");
        var parts = new List<string>();
        bool anyText = false;
        for (int i = 0; i < job.Sources.Length; i++)
        {
            string source = job.Sources[i];
            if (!File.Exists(source)) throw new FileNotFoundException("Kaynak dosya bulunamadı.", source);
            string text;
            if (job.Kind == ProcessingKind.AudioSummary)
            {
                string cache = AudioTranscriptCache.FilePath(job.Settings.TranscriptFolder, source);
                string? existing = File.Exists(cache) ? cache : null;
                progress($"STT · {i + 1}/{job.Sources.Length} · {Path.GetFileName(source)}" + (existing == null ? " · başladı" : " · mevcut metin kullanılıyor"));
                text = existing == null ? await transcribe(source, token) : await File.ReadAllTextAsync(existing, token);
                Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
                await File.WriteAllTextAsync(cache + ".tmp", text, new UTF8Encoding(false), token); File.Move(cache + ".tmp", cache, true);
            }
            else
            {
                progress($"Metinler okunuyor · {i + 1}/{job.Sources.Length} · {Path.GetFileName(source)}");
                text = await File.ReadAllTextAsync(source, token);
            }
            parts.Add($"## Bölüm {i + 1}: {Path.GetFileName(source)}\n\n{text}");
            anyText |= !string.IsNullOrWhiteSpace(text);
        }
        string combined = string.Join("\n\n---\n\n", parts);
        if (!anyText) throw new IOException("Seçilen kaynaklarda özetlenecek metin yok.");
        progress("Metinler seçilen sırayla birleştiriliyor");
        string work = Path.Combine(job.Settings.TranscriptFolder, ".vocrec2", "jobs", job.Id);
        Directory.CreateDirectory(work);
        string nameForSummary = RecordingFileNames.Normalize(job.Title) ?? "İstek_" + job.Id;
        string audioName = RecordingDates.WithPrefix(nameForSummary, job.RecordedOn);
        string merged = Path.Combine(work, audioName + ".md");
        await File.WriteAllTextAsync(merged, combined, new UTF8Encoding(false), token);
        progress("Metin kaydedildi: " + merged);
        var processor = new RecordingPostProcessor(http, (_, _) => throw new InvalidOperationException("Birleştirilmiş metin mevcut olmalı."), resolveBearerToken);
        await processor.ProcessAsync(Path.Combine(work, audioName + ".m4a"), job.Settings with { TranscriptFolder = work }, progress, token, useRecordingName: !string.IsNullOrWhiteSpace(job.Title));
    }
}
