using System.Net;
using System.Text;
using System.Text.Json;
using SesKayit.Core;
using Xunit;

namespace SesKayit.Tests;

public class RecordingDatesTests
{
    [Theory]
    [InlineData("231126_Toplantı.m4a", 2026, 11, 23)]
    [InlineData("311225_Toplantı.m4a", 2025, 12, 31)]
    [InlineData("010126_Toplantı.m4a", 2026, 1, 1)]
    [InlineData("Kayit_2024-02-29_23-59-59_abc123.m4a", 2024, 2, 29)]
    public void ReadsRecordingDateFromNewAndLegacyAudioNames(string audio, int year, int month, int day)
        => Assert.Equal(new DateOnly(year, month, day), RecordingDates.FromAudio(audio));

    [Theory]
    [InlineData("Toplantı", "231126_Toplantı")]
    [InlineData("231126_Toplantı", "231126_Toplantı")]
    [InlineData("231126_231126_Toplantı", "231126_Toplantı")]
    public void AppliesExactlyOneDayMonthYearPrefix(string name, string expected)
        => Assert.Equal(expected, RecordingDates.WithPrefix(name, new DateOnly(2026, 11, 23)));

    [Fact]
    public async Task RemovesDatePrefixWithoutTruncatingAManualNameOrApplyingTheLlmWordLimit()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string name = "Bir iki üç dört beş altı yedi " + new string('a', 90);
            var date = new DateOnly(2026, 11, 23);
            string output = await SummaryFileNames.WriteUniqueAsync(root, RecordingDates.WithPrefix(name, date), "özet", default, preserveRecordingName: true, recordedOn: date);
            Assert.Equal(name + ".ozet.md", Path.GetFileName(output));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SummaryRetryUsesTheOriginalRecordingDateEvenWhenTheFileWasCreatedAnotherDay(bool manual)
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string audio = Path.Combine(root, "311225_Proje Toplantısı.m4a");
        await File.WriteAllTextAsync(audio, "audio created today, recorded on 31 December 2025");
        var settings = new PostProcessingSettings { TranscriptFolder = root, SummaryFolder = root, LlmUrl = "http://localhost", LlmModel = "qwen", LlmPrompt = "Özetle" };
        int requests = 0, stt = 0;
        using var handler = new Handler(async request =>
        {
            requests++;
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            string prompt = payload.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
            if (requests == 1) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            if (requests == 2) Assert.Equal("Özetle", prompt);
            else { Assert.False(manual); Assert.Contains("en fazla 5", prompt); }
            string content = requests == 2 ? "# Proje özeti" : "311225_Proje Kararları";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } }), Encoding.UTF8, "application/json")
            };
        });
        using var http = new HttpClient(handler);
        try
        {
            var first = new RecordingPostProcessor(http, (_, _) => { stt++; return Task.FromResult("Konuşma metni"); });
            await Assert.ThrowsAsync<HttpRequestException>(() => first.ProcessAsync(audio, settings, _ => { }, useRecordingName: manual));
            var resumed = new RecordingPostProcessor(http, (_, _) => throw new Exception("Saved transcript must be reused"));
            await resumed.ProcessAsync(audio, settings, _ => { }, useRecordingName: manual);
            await resumed.ProcessAsync(audio, settings, _ => { }, useRecordingName: manual);
            string summary = Assert.Single(Directory.GetFiles(root, "*.ozet.md", SearchOption.AllDirectories));
            Assert.Equal(manual ? "Proje Toplantısı.ozet.md" : "Proje Kararları.ozet.md", Path.GetFileName(summary));
            Assert.Equal(Path.Combine(root, "311225"), Path.GetDirectoryName(summary));
            Assert.Equal("# Proje özeti", await File.ReadAllTextAsync(summary));
            Assert.True(File.Exists(Path.Combine(root, "311225_Proje Toplantısı.md")));
            Assert.Equal(1, stt); Assert.Equal(manual ? 2 : 3, requests);
            Assert.DoesNotContain(Directory.GetFiles(root), file => Path.GetFileName(file).StartsWith("311225_311225_", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request);
    }
}
