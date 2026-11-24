using System.Globalization;
using System.Text.RegularExpressions;

namespace SesKayit.Core;

public static class RecordingDates
{
    public static string SummaryFolder(string root, DateOnly date) => Path.Combine(root, date.ToString("ddMMyy", CultureInfo.InvariantCulture));
    public static string Prefix(DateOnly date) => date.ToString("ddMMyy", CultureInfo.InvariantCulture) + "_";

    public static string WithoutPrefix(string name, DateOnly date)
    {
        string prefix = Prefix(date);
        while (name.StartsWith(prefix, StringComparison.Ordinal)) name = name[prefix.Length..];
        return name;
    }

    public static string WithPrefix(string name, DateOnly date) => Prefix(date) + WithoutPrefix(name, date);

    public static DateOnly FromAudio(string audio)
    {
        string name = Path.GetFileNameWithoutExtension(audio);
        if (name.Length > 7 && name[6] == '_' &&
            DateOnly.TryParseExact(name[..6], "ddMMyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date;
        // Previous versions embedded the full local recording date in automatic names.
        var legacy = Regex.Match(name, @"^Kayit_(\d{4}-\d{2}-\d{2})_");
        if (legacy.Success && DateOnly.TryParseExact(legacy.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return date;
        return DateOnly.FromDateTime(File.Exists(audio) ? File.GetCreationTime(audio) : DateTime.Now);
    }
}
