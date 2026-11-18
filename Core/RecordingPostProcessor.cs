using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace SesKayit.Core;

public sealed class RecordingPostProcessor(HttpClient http, Func<string, CancellationToken, Task<string>> transcribe)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task ProcessAsync(string audio, PostProcessingSettings settings, Action<string> progress, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            progress("STT · " + Path.GetFileName(audio));
            Directory.CreateDirectory(settings.TranscriptFolder);
            string name = Path.GetFileNameWithoutExtension(audio);
            string transcript = Path.Combine(settings.TranscriptFolder, name + ".txt");
            // Reprocessing an already transcribed recording can retry an unavailable LLM.
            string text = File.Exists(transcript) ? await File.ReadAllTextAsync(transcript, token) : await transcribe(audio, token);
            await WriteAtomicAsync(transcript, text, token);
            progress("Metin kaydedildi: " + transcript);
            var endpoint = settings.Endpoint();
            if (endpoint == null || string.IsNullOrWhiteSpace(text)) return;
            progress("LLM özeti hazırlanıyor · " + name);
            using var response = await http.PostAsJsonAsync(endpoint, new
            {
                model = settings.LlmModel.Trim(), stream = false, temperature = 0.2,
                messages = new[] { new { role = "system", content = settings.LlmPrompt }, new { role = "user", content = text } }
            }, token);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"LLM HTTP {(int)response.StatusCode}. Metin korundu: {transcript}");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            string? summary = json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(summary)) throw new IOException("LLM boş özet döndürdü. Metin dosyası korundu.");
            Directory.CreateDirectory(settings.SummaryFolder);
            string output = Path.Combine(settings.SummaryFolder, name + ".ozet.txt");
            await WriteAtomicAsync(output, summary, token);
            progress("Özet kaydedildi: " + output);
        }
        finally { gate.Release(); }
    }

    private static async Task WriteAtomicAsync(string path, string text, CancellationToken token)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temp, text, new UTF8Encoding(false), token); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
