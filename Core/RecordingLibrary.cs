namespace SesKayit.Core;

public sealed record LibraryFile(string Path, string RelativeName, DateTime Modified, long Bytes);

public static class RecordingLibrary
{
    public static string[] AudioExtensions { get; } = [".m4a", ".wav", ".mp3", ".flac", ".ogg", ".aac", ".wma"];
    public static Task<LibraryFile[]> ScanAsync(string folder, bool audio, CancellationToken token = default) => Task.Run(() =>
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Klasör bulunamadı: " + folder);
        var result = new List<LibraryFile>();
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System };
        foreach (string path in Directory.EnumerateFiles(folder, "*", options))
        {
            token.ThrowIfCancellationRequested();
            string relative = System.IO.Path.GetRelativePath(folder, path);
            if (relative.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar).Any(p => p.Equals(".vocrec2", StringComparison.OrdinalIgnoreCase))) continue;
            if (path.EndsWith(".partial.m4a", StringComparison.OrdinalIgnoreCase)) continue;
            string extension = System.IO.Path.GetExtension(path);
            if (audio ? !AudioExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) : !extension.Equals(".md", StringComparison.OrdinalIgnoreCase)) continue;
            try { var info = new FileInfo(path); result.Add(new(info.FullName, relative, info.LastWriteTime, info.Length)); }
            catch (IOException) { }
        }
        return result.OrderBy(f => f.Modified).ThenBy(f => f.RelativeName, StringComparer.OrdinalIgnoreCase).ToArray();
    }, token);
}
