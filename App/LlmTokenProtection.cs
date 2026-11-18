using System.Security.Cryptography;
using System.Text;
using SesKayit.Core;

namespace SesKayit;

public static class LlmTokenProtection
{
    public static string Protect(string token)
    {
        token = LlmAuthentication.NormalizeBearerToken(token);
        if (token.Length == 0) return "";
        byte[] bytes = Encoding.UTF8.GetBytes(token);
        try { return Convert.ToBase64String(ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public static string Unprotect(string protectedToken)
    {
        if (string.IsNullOrEmpty(protectedToken)) return "";
        byte[]? bytes = null;
        try
        {
            bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedToken), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            throw new InvalidOperationException("Kaydedilmiş LLM token bu Windows hesabında açılamadı. Token alanını yeniden doldurup kaydedin.");
        }
        finally { if (bytes != null) CryptographicOperations.ZeroMemory(bytes); }
    }
}
