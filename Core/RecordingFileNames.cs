using System.Text.RegularExpressions;

namespace SesKayit.Core;

public static class RecordingFileNames
{
    public static string? Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string name = Regex.Replace(raw, "[<>:\"/\\\\|?*\\x00-\\x1F]", " ");
        name = Regex.Replace(name, @"(?<!\S)\.+(?!\S)", " ");
        name = Regex.Replace(name, @"\s+", " ").Trim(' ', '.');
        if (name.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        name = name.Trim().TrimEnd('.', ' ');
        if (name.Length > 120)
        {
            int length = char.IsHighSurrogate(name[119]) ? 119 : 120;
            name = name[..length].TrimEnd('.', ' ');
        }
        if (name.Length == 0) name = "Kayıt";
        if (Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", RegexOptions.IgnoreCase)) name = "Kayıt " + name;
        return name;
    }

    public static string RenameCompleted(string audio, string requestedName, PostProcessingSettings settings)
    {
        string title = Normalize(requestedName) ?? throw new ArgumentException("Kayıt adı boş olamaz.");
        string original = Path.GetFullPath(audio);
        string folder = Path.GetDirectoryName(original)!;
        string[] outputFolders = [folder, settings.TranscriptFolder, settings.SummaryFolder];
        string[] extensions = [".m4a", ".partial.m4a", ".md", ".txt", ".ozet.md", ".ozet.txt"];
        for (int index = 1; index <= 10000; index++)
        {
            string name = title + (index == 1 ? "" : "_" + index);
            string output = Path.Combine(folder, name + ".m4a");
            bool occupied = outputFolders.Any(directory => extensions.Any(extension =>
            {
                string candidate = Path.GetFullPath(Path.Combine(directory, name + extension));
                return !string.Equals(candidate, original, StringComparison.OrdinalIgnoreCase) && Path.Exists(candidate);
            }) || Path.Exists(Path.Combine(directory, ".vocrec2", name + ".summary.json")));
            if (occupied) continue;
            if (string.Equals(output, original, StringComparison.OrdinalIgnoreCase)) return audio;
            try { File.Move(audio, output); return output; }
            catch (IOException) when (Path.Exists(output)) { /* Another writer won this name; try the next suffix. */ }
        }
        throw new IOException("Kayıt için boş bir dosya adı bulunamadı. Ses özgün adıyla korundu.");
    }
}
