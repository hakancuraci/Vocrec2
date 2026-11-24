using System.Net;
using System.Text;
using System.Text.Json;
using SesKayit.Core;
using Xunit;

namespace SesKayit.Tests;

public class TranscriptBatchTests
{
    [Fact]
    public async Task TextSelectionKeepsOrderUsesOneSummaryAndANewJobUsesTheChangedPrompt()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string a = Path.Combine(root, "A.md"), b = Path.Combine(root, "B.md");
        await File.WriteAllTextAsync(a, "Birinci bölüm"); await File.WriteAllTextAsync(b, "İkinci bölüm");
        var requests = new List<(string Prompt, string Text)>();
        using var http = new HttpClient(new Handler(async request =>
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()); var messages = json.RootElement.GetProperty("messages");
            requests.Add((messages[1].GetProperty("content").GetString()!, messages[2].GetProperty("content").GetString()!)); return Reply("# Özet " + requests.Count);
        }));
        var settings = new PostProcessingSettings { TranscriptFolder = Path.Combine(root, "texts"), SummaryFolder = Path.Combine(root, "summaries"), LlmUrl = "http://localhost", LlmModel = "qwen", LlmPrompt = "İlk prompt" };
        var job = new ProcessingJob { Kind = ProcessingKind.TextSummary, Sources = [b, a], Settings = settings, RecordedOn = new(2026, 11, 24), Title = "Birleşik Toplantı" };
        var processor = new TranscriptBatchProcessor(http, (_, _) => throw new Exception("Text jobs must not transcribe audio"));
        try
        {
            await processor.ProcessAsync(job, _ => { }); await processor.ProcessAsync(job, _ => { });
            Assert.Single(requests); Assert.Equal("İlk prompt", requests[0].Prompt);
            Assert.True(requests[0].Text.IndexOf("İkinci bölüm", StringComparison.Ordinal) < requests[0].Text.IndexOf("Birinci bölüm", StringComparison.Ordinal));
            await processor.ProcessAsync(job with { Id = Guid.NewGuid().ToString("N"), Settings = settings with { LlmPrompt = "Yeni prompt" } }, _ => { });
            Assert.Equal(2, requests.Count); Assert.Equal("Yeni prompt", requests[1].Prompt);
            Assert.Equal("# Özet 1", await File.ReadAllTextAsync(Path.Combine(settings.SummaryFolder, "241126", "Birleşik Toplantı.ozet.md")));
            Assert.Equal("# Özet 2", await File.ReadAllTextAsync(Path.Combine(settings.SummaryFolder, "241126", "Birleşik Toplantı_2.ozet.md")));
            Assert.Equal("Birinci bölüm", await File.ReadAllTextAsync(a)); Assert.Equal("İkinci bölüm", await File.ReadAllTextAsync(b));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task AudioPartsWithTheSameFilenameUseSeparateTranscriptsAndReuseThemOnNewSummaryJobs()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "one")); Directory.CreateDirectory(Path.Combine(root, "two"));
        string a = Path.Combine(root, "one", "audio.wav"), b = Path.Combine(root, "two", "audio.wav");
        await File.WriteAllTextAsync(a, "audio-a"); await File.WriteAllTextAsync(b, "audio-b");
        int stt = 0, calls = 0;
        using var http = new HttpClient(new Handler(async request =>
        {
            calls++; using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            string text = json.RootElement.GetProperty("messages")[2].GetProperty("content").GetString()!;
            Assert.Contains("audio-a", text); Assert.Contains("audio-b", text); return Reply("özet");
        }));
        var options = new PostProcessingSettings { TranscriptFolder = Path.Combine(root, "texts"), SummaryFolder = Path.Combine(root, "summary"), LlmUrl = "http://localhost", LlmModel = "qwen" };
        var processor = new TranscriptBatchProcessor(http, async (path, _) => { stt++; return await File.ReadAllTextAsync(path); });
        var job = new ProcessingJob { Sources = [a, b], Kind = ProcessingKind.AudioSummary, Settings = options, Title = "İki Parça", RecordedOn = new(2026, 11, 24) };
        try
        {
            await processor.ProcessAsync(job, _ => { });
            await processor.ProcessAsync(job with { Id = Guid.NewGuid().ToString("N") }, _ => { });
            Assert.Equal(2, stt); Assert.Equal(2, calls);
            Assert.Equal("audio-a", await File.ReadAllTextAsync(a)); Assert.Equal("audio-b", await File.ReadAllTextAsync(b));
            Assert.Empty(await RecordingLibrary.ScanAsync(options.TranscriptFolder, false));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task AutomaticRecordingTranscriptIsReusedByManualAudioSummary()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string audio = Path.Combine(root, "241126_Toplantı.m4a"); await File.WriteAllTextAsync(audio, "audio");
        var settings = new PostProcessingSettings { TranscriptFolder = Path.Combine(root, "texts"), SummaryFolder = Path.Combine(root, "summary") };
        int stt = 0;
        using var http = new HttpClient(new Handler(_ => Task.FromResult(Reply("özet"))));
        try
        {
            await new RecordingPostProcessor(http, (_, _) => { stt++; return Task.FromResult("Önceki konuşma"); }).ProcessAsync(audio, settings, _ => { });
            var processor = new TranscriptBatchProcessor(http, (_, _) => throw new Exception("Cached recording must not run STT twice"));
            await processor.ProcessAsync(new ProcessingJob { Kind = ProcessingKind.AudioSummary, Sources = [audio], Title = "Yeni Özet", RecordedOn = new(2026, 11, 24),
                Settings = settings with { LlmUrl = "http://localhost", LlmModel = "qwen" } }, _ => { });
            Assert.Equal(1, stt);
            Assert.Single(Directory.GetFiles(Path.Combine(settings.SummaryFolder, "241126"), "*.ozet.md"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task LibraryListsNestedMarkdownAndFinishedAudioButExcludesPrivateWorkAndPartialRecordings()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "nested")); Directory.CreateDirectory(Path.Combine(root, ".vocrec2", "jobs"));
        try
        {
            foreach (string path in new[] { "a.md", "nested/B.MD", "record.m4a", "unfinished.partial.m4a", ".vocrec2/jobs/input.md", "other.txt" })
                await File.WriteAllTextAsync(Path.Combine(root, path), "text");
            Assert.Equal(2, (await RecordingLibrary.ScanAsync(root, false)).Length);
            Assert.Equal("record.m4a", Assert.Single(await RecordingLibrary.ScanAsync(root, true)).RelativeName);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task EmptyTextAndMissingLlmConfigurationFailWithoutSendingARequest()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string file = Path.Combine(root, "empty.md"); await File.WriteAllTextAsync(file, "   ");
        using var http = new HttpClient(new Handler(_ => throw new Exception("No request expected")));
        var processor = new TranscriptBatchProcessor(http, (_, _) => throw new Exception("No STT expected"));
        var job = new ProcessingJob { Sources = [file], Kind = ProcessingKind.TextSummary, Settings = new() { TranscriptFolder = root, SummaryFolder = root } };
        try
        {
            await Assert.ThrowsAsync<ArgumentException>(() => processor.ProcessAsync(job, _ => { }));
            await Assert.ThrowsAsync<IOException>(() => processor.ProcessAsync(job with { Settings = job.Settings with { LlmUrl = "http://localhost", LlmModel = "qwen" } }, _ => { }));
        }
        finally { Directory.Delete(root, true); }
    }

    private static HttpResponseMessage Reply(string content) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } }), Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }
}
