using SesKayit.Core;
using Xunit;

namespace SesKayit.Tests;

public class RecordingFileNamesTests
{
    [Theory]
    [InlineData("", null)]
    [InlineData(" \t\r\n", null)]
    [InlineData("Haftalık Proje Toplantısı", "Haftalık Proje Toplantısı")]
    [InlineData("Bir iki üç dört beş altı yedi", "Bir iki üç dört beş altı yedi")]
    [InlineData("..\\..\\Plan/Notları:*?\"<>|", "Plan Notları")]
    [InlineData("CON.txt", "Kayıt CON.txt")]
    [InlineData("LPT1", "Kayıt LPT1")]
    [InlineData("  Proje  Kararları.m4a . ", "Proje Kararları")]
    [InlineData("...??", "Kayıt")]
    public void NormalizesManualNamesWithoutApplyingTheLlmWordLimit(string input, string? expected)
        => Assert.Equal(expected, RecordingFileNames.Normalize(input));

    [Fact]
    public void LongNamesRemainSafeWithoutSplittingUnicodeCharacters()
    {
        string input = new('a', 119);
        string result = RecordingFileNames.Normalize(input + "🎤 toplantı")!;
        Assert.Equal(input, result);
    }

    [Fact]
    public void RenamingChecksAudioTranscriptsSummariesAndSavedJobsBeforeChoosingASuffix()
    {
        string root = Path.Combine(Path.GetTempPath(), "vocrec-names-" + Guid.NewGuid().ToString("N"));
        var settings = new PostProcessingSettings { TranscriptFolder = Path.Combine(root, "text"), SummaryFolder = Path.Combine(root, "summary") };
        Directory.CreateDirectory(Path.Combine(settings.TranscriptFolder, ".vocrec2")); Directory.CreateDirectory(settings.SummaryFolder);
        try
        {
            string audio = Path.Combine(root, "231126_automatic.m4a");
            File.WriteAllText(audio, "new audio");
            File.WriteAllText(Path.Combine(root, "231126_Toplantı.m4a"), "old audio");
            File.WriteAllText(Path.Combine(settings.TranscriptFolder, "231126_Toplantı_2.md"), "old transcript");
            File.WriteAllText(Path.Combine(settings.SummaryFolder, "231126_Toplantı_3.ozet.md"), "old summary");
            File.WriteAllText(Path.Combine(settings.TranscriptFolder, ".vocrec2", "231126_Toplantı_4.summary.json"), "old checkpoint");
            string renamed = RecordingFileNames.RenameCompleted(audio, "Toplantı", settings);
            Assert.Equal(Path.Combine(root, "231126_Toplantı_5.m4a"), renamed);
            Assert.False(File.Exists(audio)); Assert.Equal("new audio", File.ReadAllText(renamed));
            Assert.Equal("old audio", File.ReadAllText(Path.Combine(root, "231126_Toplantı.m4a")));
            Assert.Equal("old transcript", File.ReadAllText(Path.Combine(settings.TranscriptFolder, "231126_Toplantı_2.md")));
            Assert.Equal("old summary", File.ReadAllText(Path.Combine(settings.SummaryFolder, "231126_Toplantı_3.ozet.md")));
            Assert.Equal(renamed, RecordingFileNames.RenameCompleted(renamed, "Toplantı_5", settings));
        }
        finally { Directory.Delete(root, true); }
    }
}
