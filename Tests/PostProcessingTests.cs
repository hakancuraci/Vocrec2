using System.Net;
using System.Text;
using System.Text.Json;
using SesKayit.Core;
using Xunit;

namespace SesKayit.Tests;

public class PostProcessingTests
{
    [Theory]
    [InlineData("", null)]
    [InlineData("http://localhost:8000", "http://localhost:8000/v1/chat/completions")]
    [InlineData("http://localhost:8000/v1/", "http://localhost:8000/v1/chat/completions")]
    [InlineData("https://host/prefix/v1/chat/completions", "https://host/prefix/v1/chat/completions")]
    public void ResolvesEndpoint(string input, string? expected) => Assert.Equal(expected, new PostProcessingSettings { LlmUrl = input }.Endpoint()?.ToString());

    [Theory]
    [InlineData("file:///tmp/service")]
    [InlineData("http://localhost?model=qwen")]
    [InlineData("not a url")]
    public void RejectsInvalidEndpoints(string url) => Assert.Throws<ArgumentException>(() => new PostProcessingSettings { LlmUrl = url }.Validate());

    [Fact]
    public void SettingsRoundTripPreservesAllOptions()
    {
        var settings = new PostProcessingSettings { TranscriptFolder = "texts", SummaryFolder = "summaries", LlmUrl = "http://localhost:8000/v1", LlmModel = "qwen", LlmPrompt = "Özel prompt", EnhanceMicrophone = false };
        Assert.Equal(settings, JsonSerializer.Deserialize<PostProcessingSettings>(JsonSerializer.Serialize(settings)));
    }

    [Fact]
    public async Task WritesTranscriptBeforeSendingAndSavesSummary()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new PostProcessingSettings { TranscriptFolder = root, SummaryFolder = Path.Combine(root, "summary"), LlmUrl = "http://localhost:8000/v1", LlmModel = "qwen-test", LlmPrompt = "Özetle" };
            int calls = 0;
            using var handler = new Handler(async request =>
            {
                Assert.Equal("Konuşma metni", await File.ReadAllTextAsync(Path.Combine(root, Dated("record.md"))));
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Assert.Equal("qwen-test", payload.RootElement.GetProperty("model").GetString());
                Assert.False(payload.RootElement.GetProperty("stream").GetBoolean());
                Assert.True(payload.RootElement.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
                var messages = payload.RootElement.GetProperty("messages");
                Assert.Equal(3, messages.GetArrayLength());
                Assert.Equal("system", messages[0].GetProperty("role").GetString());
                Assert.Equal("Answer the question asked.", messages[0].GetProperty("content").GetString());
                Assert.Equal("user", messages[1].GetProperty("role").GetString());
                Assert.Equal("user", messages[2].GetProperty("role").GetString());
                if (calls++ == 0)
                {
                    Assert.Equal("Özetle", messages[1].GetProperty("content").GetString());
                    Assert.Equal("Konuşma metni", messages[2].GetProperty("content").GetString());
                    return Reply("Özet metni");
                }
                Assert.Contains("en fazla 5", messages[1].GetProperty("content").GetString());
                Assert.Equal("Özet metni", messages[2].GetProperty("content").GetString());
                return Reply("Proje Planlama Kararları");
            });
            using var http = new HttpClient(handler);
            await new RecordingPostProcessor(http, (_, _) => Task.FromResult("Konuşma metni")).ProcessAsync("record.m4a", settings, _ => { });
            Assert.Equal(2, calls);
            Assert.Equal("Özet metni", await File.ReadAllTextAsync(Path.Combine(root, "summary", Dated("Proje Planlama Kararları.ozet.md"))));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task UnavailableLlmPreservesTranscriptAndCanRetryWithoutStt()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            using var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
            using var http = new HttpClient(handler);
            var settings = new PostProcessingSettings { TranscriptFolder = root, SummaryFolder = root, LlmUrl = "http://localhost", LlmModel = "qwen" };
            int calls = 0;
            var processor = new RecordingPostProcessor(http, (_, _) => { calls++; return Task.FromResult("Kalıcı metin"); });
            for (int i = 0; i < 2; i++) await Assert.ThrowsAsync<HttpRequestException>(() => processor.ProcessAsync("record.m4a", settings, _ => { }));
            Assert.Equal(1, calls);
            Assert.Equal("Kalıcı metin", await File.ReadAllTextAsync(Path.Combine(root, Dated("record.md"))));
            Assert.False(File.Exists(Path.Combine(root, Dated("record.ozet.md"))));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task EmptyUrlRunsSttWithoutNetwork()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            using var http = new HttpClient(new Handler(_ => throw new Exception("Unexpected network call")));
            await new RecordingPostProcessor(http, (_, _) => Task.FromResult("metin")).ProcessAsync("record.m4a", new() { TranscriptFolder = root, SummaryFolder = root }, _ => { });
            Assert.Equal("metin", File.ReadAllText(Path.Combine(root, Dated("record.md"))));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("abc.def-123", "abc.def-123")]
    [InlineData(" Bearer abc.def-123 ", "abc.def-123")]
    [InlineData("bearer abc", "abc")]
    public void NormalizesBearerToken(string input, string expected) => Assert.Equal(expected, LlmAuthentication.NormalizeBearerToken(input));

    [Theory]
    [InlineData("abc\r\nInjected: value")]
    [InlineData("abc def")]
    [InlineData("Bearer")]
    public void RejectsMalformedBearerTokens(string input) => Assert.Throws<ArgumentException>(() => LlmAuthentication.NormalizeBearerToken(input));

    [Fact]
    public async Task BearerTokenIsSentOnlyForTheConfiguredRequest()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            string configuredToken = "Bearer fake-test-token";
            int calls = 0;
            using var handler = new Handler(request =>
            {
                if (calls++ < 2)
                {
                    Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                    Assert.Equal("fake-test-token", request.Headers.Authorization?.Parameter);
                }
                else Assert.Null(request.Headers.Authorization);
                Assert.Empty(request.RequestUri!.Query);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"Özet\"}}]}", Encoding.UTF8, "application/json") });
            });
            using var http = new HttpClient(handler);
            var settings = new PostProcessingSettings { TranscriptFolder = root, SummaryFolder = root, LlmUrl = "http://localhost", LlmModel = "qwen" };
            var processor = new RecordingPostProcessor(http, (_, _) => Task.FromResult("metin"), _ => configuredToken);
            await processor.ProcessAsync("record-one.m4a", settings, _ => { });
            configuredToken = "";
            await processor.ProcessAsync("record-two.m4a", settings, _ => { });
            Assert.Equal(4, calls);
            Assert.Null(http.DefaultRequestHeaders.Authorization);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task FailedTitleRequestPreservesSummaryAndResumesAfterRestart()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new PostProcessingSettings { TranscriptFolder = root, SummaryFolder = root, LlmUrl = "http://localhost", LlmModel = "qwen", LlmPrompt = "Özetle" };
            int calls = 0, stt = 0;
            using var handler = new Handler(_ => Task.FromResult(++calls switch
            {
                1 => Reply("Korunan özet"),
                2 => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                _ => Reply("Toplantı Kararları ve Sonraki Adımlar Fazla Kelime")
            }));
            using var http = new HttpClient(handler);
            var first = new RecordingPostProcessor(http, (_, _) => { stt++; return Task.FromResult("metin"); });
            await Assert.ThrowsAsync<IOException>(() => first.ProcessAsync("record.m4a", settings, _ => { }));
            Assert.Equal("Korunan özet", await File.ReadAllTextAsync(Path.Combine(root, Dated("record.ozet.md"))));
            var resumed = new RecordingPostProcessor(http, (_, _) => throw new Exception("STT must not repeat"));
            await resumed.ProcessAsync("record.m4a", settings, _ => { });
            string output = Assert.Single(Directory.GetFiles(root, "*.ozet.md"));
            Assert.EndsWith("Toplantı Kararları ve Sonraki Adımlar.ozet.md", output);
            Assert.Equal("Korunan özet", await File.ReadAllTextAsync(output));
            Assert.Equal(1, stt); Assert.Equal(3, calls);
            await resumed.ProcessAsync("record.m4a", settings, _ => { });
            Assert.Equal(3, calls); // A completed retry does not repeat either model call.
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DuplicateTitlesNeverOverwriteAnotherRecording()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new PostProcessingSettings { TranscriptFolder = root, SummaryFolder = root, LlmUrl = "http://localhost", LlmModel = "qwen", LlmPrompt = "Özetle" };
            using var handler = new Handler(async request =>
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                var messages = json.RootElement.GetProperty("messages");
                return Reply(messages[1].GetProperty("content").GetString() == "Özetle" ? messages[2].GetProperty("content").GetString()! : "Haftalık Proje Toplantısı");
            });
            using var http = new HttpClient(handler);
            var processor = new RecordingPostProcessor(http, (audio, _) => Task.FromResult(audio));
            await processor.ProcessAsync("first.m4a", settings, _ => { });
            await processor.ProcessAsync("second.m4a", settings, _ => { });
            Assert.Equal("first.m4a", File.ReadAllText(Path.Combine(root, Dated("Haftalık Proje Toplantısı.ozet.md"))));
            Assert.Equal("second.m4a", File.ReadAllText(Path.Combine(root, Dated("Haftalık Proje Toplantısı_2.ozet.md"))));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task LegacyTextDraftResumesIntoMarkdownWithoutRepeatingSttOrSummary()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const string markdown = "# Proje Özeti\n\n- Karar: devam et.\n";
            await File.WriteAllTextAsync(Path.Combine(root, "record.txt"), "Önceki konuşma");
            await File.WriteAllTextAsync(Path.Combine(root, "record.ozet.txt"), markdown);
            var settings = new PostProcessingSettings { TranscriptFolder = root, SummaryFolder = root, LlmUrl = "http://localhost", LlmModel = "qwen" };
            int calls = 0;
            using var handler = new Handler(async request =>
            {
                calls++;
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                var messages = payload.RootElement.GetProperty("messages");
                Assert.Contains("en fazla 5", messages[1].GetProperty("content").GetString());
                Assert.Equal(markdown, messages[2].GetProperty("content").GetString());
                return Reply("Proje Devam Kararları");
            });
            using var http = new HttpClient(handler);
            var processor = new RecordingPostProcessor(http, (_, _) => throw new Exception("Existing transcript must be reused"));
            await processor.ProcessAsync("record.m4a", settings, _ => { });
            Assert.Equal(1, calls);
            Assert.Equal("Önceki konuşma", await File.ReadAllTextAsync(Path.Combine(root, Dated("record.md"))));
            Assert.Equal(markdown, await File.ReadAllTextAsync(Path.Combine(root, Dated("Proje Devam Kararları.ozet.md"))));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CompletedLegacySummaryMigratesAndRemainsIdempotentWithoutLlm()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".vocrec2"));
        try
        {
            const string markdown = "## Kararlar\n\n| İş | Durum |\n| --- | --- |\n| Test | Tamam |\n";
            string oldSummary = Path.Combine(root, "Önceki Toplantı.ozet.txt");
            await File.WriteAllTextAsync(Path.Combine(root, "record.txt"), "önceki metin");
            await File.WriteAllTextAsync(oldSummary, markdown);
            await File.WriteAllTextAsync(Path.Combine(root, ".vocrec2", "record.summary.json"), JsonSerializer.Serialize(new { Summary = markdown, Title = "Önceki Toplantı", OutputPath = oldSummary }));
            using var http = new HttpClient(new Handler(_ => throw new Exception("No model call needed for a completed summary")));
            var settings = new PostProcessingSettings { TranscriptFolder = root, SummaryFolder = root, LlmUrl = "http://localhost", LlmModel = "qwen" };
            var processor = new RecordingPostProcessor(http, (_, _) => throw new Exception("No STT needed"));
            await processor.ProcessAsync("record.m4a", settings, _ => { });
            await processor.ProcessAsync("record.m4a", settings, _ => { });
            string output = Assert.Single(Directory.GetFiles(root, "*.ozet.md"));
            Assert.Equal(markdown, await File.ReadAllTextAsync(output));
            Assert.True(File.Exists(oldSummary));
            using var state = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, ".vocrec2", "record.summary.json")));
            Assert.Equal(output, state.RootElement.GetProperty("OutputPath").GetString());
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    public async Task ManualRecordingNameSkipsOnlyTheTitleRequestAndKeepsMarkdown(bool useRecordingName, int expectedRequests)
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        const string name = "Bir iki üç dört beş altı yedi";
        const string summary = "# Özet\n\n- Karar: devam et.\n";
        var settings = new PostProcessingSettings { TranscriptFolder = root, SummaryFolder = root, LlmUrl = "http://localhost", LlmModel = "qwen", LlmPrompt = "Özetle" };
        int requests = 0, stt = 0;
        using var handler = new Handler(async request =>
        {
            requests++;
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var messages = payload.RootElement.GetProperty("messages");
            if (requests == 1)
            {
                Assert.Equal("Özetle", messages[1].GetProperty("content").GetString());
                Assert.Equal("Konuşma metni", messages[2].GetProperty("content").GetString());
                return Reply(summary);
            }
            Assert.False(useRecordingName, "A user-named recording must never request an LLM filename.");
            Assert.Equal(summary, messages[2].GetProperty("content").GetString());
            return Reply("Otomatik Toplantı Adı");
        });
        using var http = new HttpClient(handler);
        try
        {
            var processor = new RecordingPostProcessor(http, (_, _) => { stt++; return Task.FromResult("Konuşma metni"); });
            await processor.ProcessAsync(name + ".m4a", settings, _ => { }, useRecordingName: useRecordingName);
            string expected = Path.Combine(root, Dated((useRecordingName ? name : "Otomatik Toplantı Adı") + ".ozet.md"));
            Assert.Equal(summary, await File.ReadAllTextAsync(expected));
            Assert.Equal("Konuşma metni", await File.ReadAllTextAsync(Path.Combine(root, Dated(name + ".md"))));
            var restarted = new RecordingPostProcessor(http, (_, _) => throw new Exception("Completed STT must not repeat"));
            await restarted.ProcessAsync(name + ".m4a", settings, _ => { }, useRecordingName: useRecordingName);
            Assert.Equal(1, stt); Assert.Equal(expectedRequests, requests);
            Assert.Single(Directory.GetFiles(root, "*.ozet.md"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ManualNameSurvivesSummaryServiceFailureAndRestart()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var settings = new PostProcessingSettings { TranscriptFolder = root, SummaryFolder = root, LlmUrl = "http://localhost", LlmModel = "qwen", LlmPrompt = "Özetle" };
        int requests = 0;
        using var handler = new Handler(async request =>
        {
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("Özetle", payload.RootElement.GetProperty("messages")[1].GetProperty("content").GetString());
            return ++requests == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Reply("Korunan özet");
        });
        using var http = new HttpClient(handler);
        try
        {
            var first = new RecordingPostProcessor(http, (_, _) => Task.FromResult("metin"));
            await Assert.ThrowsAsync<HttpRequestException>(() => first.ProcessAsync("Benim Toplantım.m4a", settings, _ => { }, useRecordingName: true));
            var restarted = new RecordingPostProcessor(http, (_, _) => throw new Exception("STT must not repeat"));
            await restarted.ProcessAsync("Benim Toplantım.m4a", settings, _ => { }, useRecordingName: true);
            Assert.Equal("Korunan özet", await File.ReadAllTextAsync(Path.Combine(root, Dated("Benim Toplantım.ozet.md"))));
            Assert.Equal(2, requests);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task AConcurrentSummaryWithTheSameManualNameIsNeverReusedOrOverwritten()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string existing = Path.Combine(root, Dated("Benim Toplantım.ozet.md"));
            await File.WriteAllTextAsync(existing, "Başka kaydın özeti");
            using var http = new HttpClient(new Handler(_ => Task.FromResult(Reply("Yeni kaydın özeti"))));
            var processor = new RecordingPostProcessor(http, (_, _) => Task.FromResult("Yeni metin"));
            var settings = new PostProcessingSettings { TranscriptFolder = root, SummaryFolder = root, LlmUrl = "http://localhost", LlmModel = "qwen" };
            await processor.ProcessAsync("Benim Toplantım.m4a", settings, _ => { }, useRecordingName: true);
            Assert.Equal("Başka kaydın özeti", await File.ReadAllTextAsync(existing));
            Assert.Equal("Yeni kaydın özeti", await File.ReadAllTextAsync(Path.Combine(root, Dated("Benim Toplantım_2.ozet.md"))));
        }
        finally { Directory.Delete(root, true); }
    }

    private static string Dated(string name) => RecordingDates.WithPrefix(name, DateOnly.FromDateTime(DateTime.Now));

    private static HttpResponseMessage Reply(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } }), Encoding.UTF8, "application/json")
    };

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request);
    }
}
