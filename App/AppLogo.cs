namespace SesKayit;

internal static class AppLogo
{
    private static readonly Lazy<Icon> instance = new(() =>
    {
        using var stream = typeof(AppLogo).Assembly.GetManifestResourceStream("SesKayit.Assets.Vocrec2.ico")
            ?? throw new InvalidOperationException("Vocrec2 uygulama simgesi bulunamadı.");
        using var source = new Icon(stream, new Size(48, 48));
        return (Icon)source.Clone();
    });
    public static Icon Icon => instance.Value;
}
