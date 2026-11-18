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
            using var handler = new Handler(async request =>
            {
                Assert.Equal("Konuşma metni", await File.ReadAllTextAsync(Path.Combine(root, "record.txt")));
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Assert.Equal("qwen-test", payload.RootElement.GetProperty("model").GetString());
                Assert.False(payload.RootElement.GetProperty("stream").GetBoolean());
                Assert.True(payload.RootElement.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
                var messages = payload.RootElement.GetProperty("messages");
                Assert.Equal(3, messages.GetArrayLength());
                Assert.Equal("system", messages[0].GetProperty("role").GetString());
                Assert.Equal("Answer the question asked.", messages[0].GetProperty("content").GetString());
                Assert.Equal("user", messages[1].GetProperty("role").GetString());
                Assert.Equal("Özetle", messages[1].GetProperty("content").GetString());
                Assert.Equal("user", messages[2].GetProperty("role").GetString());
                Assert.Equal("Konuşma metni", messages[2].GetProperty("content").GetString());
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"Özet\"}}]}", Encoding.UTF8, "application/json") };
            });
            using var http = new HttpClient(handler);
            await new RecordingPostProcessor(http, (_, _) => Task.FromResult("Konuşma metni")).ProcessAsync("record.m4a", settings, _ => { });
            Assert.Equal("Özet", await File.ReadAllTextAsync(Path.Combine(root, "summary", "record.ozet.txt")));
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
            Assert.Equal("Kalıcı metin", await File.ReadAllTextAsync(Path.Combine(root, "record.txt")));
            Assert.False(File.Exists(Path.Combine(root, "record.ozet.txt")));
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
            Assert.Equal("metin", File.ReadAllText(Path.Combine(root, "record.txt")));
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
                if (calls++ == 0)
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
            await processor.ProcessAsync("record.m4a", settings, _ => { });
            configuredToken = "";
            await processor.ProcessAsync("record.m4a", settings, _ => { });
            Assert.Equal(2, calls);
            Assert.Null(http.DefaultRequestHeaders.Authorization);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request);
    }
}
