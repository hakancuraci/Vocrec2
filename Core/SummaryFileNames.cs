using System.Text.RegularExpressions;

namespace SesKayit.Core;

public static class SummaryFileNames
{
    public static string Normalize(string raw)
    {
        string line = raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).FirstOrDefault(s => s.Length > 0 && !s.StartsWith("```", StringComparison.Ordinal)) ?? "";
        line = Regex.Replace(line, @"^(dosya\s*adı|filename)\s*:\s*", "", RegexOptions.IgnoreCase);
        line = Regex.Replace(line, @"\.(ozet\.)?(txt|md)\s*[""']?\s*$", "", RegexOptions.IgnoreCase);
        string title = string.Join(' ', Regex.Matches(line, @"[\p{L}\p{N}][\p{L}\p{N}\p{M}_-]*").Select(m => m.Value).Take(5));
        if (title.Length > 100) title = title[..100].TrimEnd(' ', '-', '_');
        if (title.Length == 0) return "Toplantı Özeti";
        if (Regex.IsMatch(title, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase)) title = "Özet " + title;
        return title;
    }

    public static async Task<string> WriteUniqueAsync(string folder, string title, string summary, CancellationToken token, bool preserveRecordingName = false, DateOnly? recordedOn = null)
    {
        if (recordedOn is { } date) title = RecordingDates.WithoutPrefix(title, date);
        title = preserveRecordingName ? RecordingFileNames.Normalize(title) ?? "Kayıt" : Normalize(title);
        for (int index = 1; index <= 10000; index++)
        {
            string path = Path.Combine(folder, title + (index == 1 ? "" : "_" + index) + ".ozet.md");
            FileStream file;
            try { file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true); }
            catch (IOException) when (File.Exists(path)) { continue; }
            try
            {
                await using (file)
                await using (var writer = new StreamWriter(file, new System.Text.UTF8Encoding(false)))
                    await writer.WriteAsync(summary.AsMemory(), token);
                return path;
            }
            catch { file.Dispose(); File.Delete(path); throw; }
        }
        throw new IOException("Özet için boş bir dosya adı bulunamadı.");
    }
}
