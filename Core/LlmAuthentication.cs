namespace SesKayit.Core;

public static class LlmAuthentication
{
    public static string NormalizeBearerToken(string? value)
    {
        string token = (value ?? "").Trim();
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) token = token[7..].Trim();
        if (token.Equals("Bearer", StringComparison.OrdinalIgnoreCase) || token.Any(c => c <= ' ' || c >= 127))
            throw new ArgumentException("Bearer token boşluk veya kontrol karakteri içeremez; yalnız token değerini girin.");
        return token;
    }
}
