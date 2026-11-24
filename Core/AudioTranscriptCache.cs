using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SesKayit.Core;

public static class AudioTranscriptCache
{
    public static string FilePath(string transcriptFolder, string audio)
    {
        string path = Path.GetFullPath(audio);
        if (OperatingSystem.IsWindows()) path = path.ToUpperInvariant();
        string identity = path + "|" + new FileInfo(audio).Length.ToString(CultureInfo.InvariantCulture) + "|" + File.GetLastWriteTimeUtc(audio).Ticks.ToString(CultureInfo.InvariantCulture);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return Path.Combine(transcriptFolder, ".vocrec2", "audio", hash + ".md");
    }
}
