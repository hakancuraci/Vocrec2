using SesKayit.Core;
using Xunit;

namespace SesKayit.Tests;

public class SummaryFileNamesTests
{
    [Theory]
    [InlineData("Bir iki üç dört beş altı yedi", "Bir iki üç dört beş")]
    [InlineData("```text\nProje Planlama Kararları\n```", "Proje Planlama Kararları")]
    [InlineData("CON", "Özet CON")]
    [InlineData("LPT1", "Özet LPT1")]
    [InlineData("..\\..\\Toplantı/Planı:*?\"<>|", "Toplantı Planı")]
    [InlineData("Proje Kararları.txt", "Proje Kararları")]
    [InlineData("Dosya adı: Haftalık Proje Özeti", "Haftalık Proje Özeti")]
    [InlineData("...", "Toplantı Özeti")]
    [InlineData("", "Toplantı Özeti")]
    public void ProducesSafeShortWindowsTitle(string input, string expected)
    {
        string result = SummaryFileNames.Normalize(input);
        Assert.Equal(expected, result);
        Assert.InRange(result.Split(' ').Length, 1, 5);
        Assert.DoesNotContain(result, c => "<>:\"/\\|?*".Contains(c) || char.IsControl(c));
    }

    [Fact]
    public void ProfilesKeepOutputFoldersWhenSwitchingModels()
    {
        var settings = new PostProcessingSettings { TranscriptFolder = "texts", SummaryFolder = "summaries" };
        var profile = new LlmProfile { Name = "Local", Url = "http://localhost", Model = "qwen", Prompt = "Özel prompt", ProtectedBearerToken = "encrypted-value" };
        var applied = profile.ApplyTo(settings);
        Assert.Equal("texts", applied.TranscriptFolder); Assert.Equal("summaries", applied.SummaryFolder);
        Assert.Equal("qwen", applied.LlmModel); Assert.Equal("encrypted-value", applied.ProtectedLlmBearerToken);
        Assert.Equal("Local", profile.ToString());
    }
}
