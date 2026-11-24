using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SesKayit.Core;

public sealed class RecordingPostProcessor(HttpClient http, Func<string, CancellationToken, Task<string>> transcribe,
    Func<PostProcessingSettings, string>? resolveBearerToken = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private const string TitlePrompt = "Aşağıdaki özeti temsil eden en fazla 5 kelimelik Türkçe bir dosya adı üret. Yalnızca dosya adını yaz. Açıklama, uzantı, tırnak veya Markdown ekleme.";
    private sealed record SummaryState(string Summary, string? Title = null, string? OutputPath = null);

    public async Task ProcessAsync(string audio, PostProcessingSettings settings, Action<string> progress, CancellationToken token = default, bool useRecordingName = false)
    {
        await gate.WaitAsync(token);
        try
        {
            progress("STT · " + Path.GetFileName(audio));
            Directory.CreateDirectory(settings.TranscriptFolder);
            DateOnly recordedOn = RecordingDates.FromAudio(audio);
            string sourceName = Path.GetFileNameWithoutExtension(audio);
            string name = RecordingDates.WithPrefix(sourceName, recordedOn);
            string transcript = Path.Combine(settings.TranscriptFolder, name + ".md");
            string previousTranscript = Path.Combine(settings.TranscriptFolder, sourceName + ".md");
            string legacyTranscript = Path.Combine(settings.TranscriptFolder, sourceName + ".txt");
            string? cachedTranscript = File.Exists(transcript) ? transcript : File.Exists(previousTranscript) ? previousTranscript : File.Exists(legacyTranscript) ? legacyTranscript : null;
            string text = cachedTranscript != null ? await File.ReadAllTextAsync(cachedTranscript, token) : await transcribe(audio, token);
            await WriteAtomicAsync(transcript, text, token);
            progress("Metin kaydedildi: " + transcript);
            if (settings.Endpoint() == null || string.IsNullOrWhiteSpace(text)) return;
            string summaryFolder = RecordingDates.SummaryFolder(settings.SummaryFolder, recordedOn);
            Directory.CreateDirectory(summaryFolder);
            string stateFolder = Path.Combine(settings.TranscriptFolder, ".vocrec2");
            Directory.CreateDirectory(stateFolder);
            string statePath = Path.Combine(stateFolder, sourceName + ".summary.json");
            string fallback = Path.Combine(summaryFolder, name + ".ozet.md");
            string previousDatedFallback = Path.Combine(settings.SummaryFolder, name + ".ozet.md");
            string previousFallback = Path.Combine(settings.SummaryFolder, sourceName + ".ozet.md");
            string legacyFallback = Path.Combine(settings.SummaryFolder, sourceName + ".ozet.txt");
            SummaryState? state = null;
            if (File.Exists(statePath))
            {
                try { state = JsonSerializer.Deserialize<SummaryState>(await File.ReadAllTextAsync(statePath, token)); }
                catch (JsonException) { /* The saved draft below also allows recovery. */ }
            }
            if (state?.OutputPath != null && File.Exists(state.OutputPath))
            {
                if (!string.Equals(Path.GetExtension(state.OutputPath), ".md", StringComparison.OrdinalIgnoreCase) ||
                    !Path.GetFileName(state.OutputPath).StartsWith(RecordingDates.Prefix(recordedOn), StringComparison.Ordinal) ||
                    !string.Equals(Path.GetDirectoryName(Path.GetFullPath(state.OutputPath)), Path.GetFullPath(summaryFolder), StringComparison.OrdinalIgnoreCase))
                {
                    // Resume older completed jobs without transcribing or asking the LLM again.
                    string previous = await File.ReadAllTextAsync(state.OutputPath, token);
                    string title = state.Title ?? Path.GetFileNameWithoutExtension(state.OutputPath).Replace(".ozet", "");
                    string migrated = await SummaryFileNames.WriteUniqueAsync(summaryFolder, title, previous, token, preserveRecordingName: useRecordingName, recordedOn: recordedOn);
                    state = state with { OutputPath = migrated };
                    await WriteAtomicAsync(statePath, JsonSerializer.Serialize(state), token);
                }
                progress("Özet kaydedildi: " + state.OutputPath); return;
            }
            if (state == null)
            {
                progress("LLM özeti hazırlanıyor · " + name);
                // A manually chosen name can also be returned by another recording's LLM.
                // Its existing summary is not our cache; only our checkpoint owns a draft.
                string? cachedSummary = useRecordingName ? null : File.Exists(fallback) ? fallback : File.Exists(previousDatedFallback) ? previousDatedFallback : File.Exists(previousFallback) ? previousFallback : File.Exists(legacyFallback) ? legacyFallback : null;
                string summary = cachedSummary != null ? await File.ReadAllTextAsync(cachedSummary, token) : await CallLlmAsync(settings, settings.LlmPrompt, text, token);
                state = new SummaryState(summary);
                await WriteAtomicAsync(statePath, JsonSerializer.Serialize(state), token);
            }
            if (useRecordingName)
            {
                // The recording already owns this name. Keep audio, transcript and summary paired
                // and never send the summary back to the model merely to obtain another title.
                string namedOutput = await SummaryFileNames.WriteUniqueAsync(summaryFolder, name, state.Summary, token, preserveRecordingName: true, recordedOn: recordedOn);
                state = state with { Title = name, OutputPath = namedOutput };
                await WriteAtomicAsync(statePath, JsonSerializer.Serialize(state), token);
                progress("Özet kaydedildi: " + namedOutput); return;
            }
            // Preserve the summary before asking the model for its filename.
            await WriteAtomicAsync(fallback, state.Summary, token);
            if (state.Title == null)
            {
                progress("Özet için kısa dosya adı oluşturuluyor…");
                try
                {
                    string title = await CallLlmAsync(settings, TitlePrompt, state.Summary, token);
                    state = state with { Title = SummaryFileNames.Normalize(title) };
                    await WriteAtomicAsync(statePath, JsonSerializer.Serialize(state), token);
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or InvalidOperationException)
                {
                    throw new IOException("Özet korundu; dosya adı alınamadı. Bekleyen işlemi yeniden deneyin. Özet: " + fallback, ex);
                }
            }
            string output = await SummaryFileNames.WriteUniqueAsync(summaryFolder, state.Title, state.Summary, token, recordedOn: recordedOn);
            state = state with { OutputPath = output };
            await WriteAtomicAsync(statePath, JsonSerializer.Serialize(state), token);
            if (!string.Equals(fallback, output, StringComparison.OrdinalIgnoreCase)) File.Delete(fallback);
            progress("Özet kaydedildi: " + output);
        }
        finally { gate.Release(); }
    }

    private async Task<string> CallLlmAsync(PostProcessingSettings settings, string prompt, string text, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint());
        string bearerToken = LlmAuthentication.NormalizeBearerToken(resolveBearerToken?.Invoke(settings));
        if (bearerToken.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        request.Content = JsonContent.Create(new
        {
            model = settings.LlmModel.Trim(), stream = false,
            chat_template_kwargs = new { enable_thinking = true },
            messages = new[]
            {
                new { role = "system", content = "Answer the question asked." },
                new { role = "user", content = prompt },
                new { role = "user", content = text }
            }
        });
        using var response = await http.SendAsync(request, token);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"LLM HTTP {(int)response.StatusCode}. Kaydedilmiş metin ve özetler korundu.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        string? content = json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        if (string.IsNullOrWhiteSpace(content)) throw new IOException("LLM boş yanıt döndürdü. Kaydedilmiş dosyalar korundu.");
        return content;
    }

    private static async Task WriteAtomicAsync(string path, string text, CancellationToken token)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temp, text, new UTF8Encoding(false), token); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
