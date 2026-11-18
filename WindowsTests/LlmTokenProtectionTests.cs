using System.Text.Json;
using SesKayit.Core;
using Xunit;

namespace SesKayit.WindowsTests;

public class LlmTokenProtectionTests
{
    [Fact]
    public void TokenSurvivesSettingsAndPendingJobSerializationWithoutPlaintext()
    {
        const string token = "fake-persistent-test-token-123";
        var settings = new PostProcessingSettings { ProtectedLlmBearerToken = LlmTokenProtection.Protect("Bearer " + token) };
        string json = JsonSerializer.Serialize(settings);
        Assert.DoesNotContain(token, json);
        var restored = JsonSerializer.Deserialize<PostProcessingSettings>(json)!;
        Assert.Equal(token, LlmTokenProtection.Unprotect(restored.ProtectedLlmBearerToken));
        string pending = JsonSerializer.Serialize(new { Audio = "record.m4a", Settings = settings });
        Assert.DoesNotContain(token, pending);
        using var document = JsonDocument.Parse(pending);
        var queued = document.RootElement.GetProperty("Settings").Deserialize<PostProcessingSettings>()!;
        Assert.Equal(token, LlmTokenProtection.Unprotect(queued.ProtectedLlmBearerToken));
    }

    [Fact]
    public void EmptyTokenNeedsNoStoredCredential()
    {
        Assert.Empty(LlmTokenProtection.Protect(""));
        Assert.Empty(LlmTokenProtection.Unprotect(""));
    }

    [Fact]
    public void DamagedCredentialShowsSafeRecoveryMessage()
    {
        var error = Assert.Throws<InvalidOperationException>(() => LlmTokenProtection.Unprotect("not-base64!"));
        Assert.Contains("Token alanını yeniden doldurup kaydedin", error.Message);
        Assert.DoesNotContain("not-base64!", error.Message);
    }
}
