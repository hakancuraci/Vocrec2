using System.Reflection;

namespace SesKayit;

internal static class AppVersion
{
    private static readonly string[] parts = (typeof(AppVersion).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AppVersion).Assembly.GetName().Version?.ToString()
        ?? "bilinmiyor").Split('+', 2);

    internal static string Number => parts[0];
    internal static string Commit => parts.Length == 2 ? parts[1] : "kaynak bilgisi yok";
    internal static string ShortCommit => parts.Length == 2 ? Commit[..Math.Min(8, Commit.Length)] : Commit;
}
