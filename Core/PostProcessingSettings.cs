namespace SesKayit.Core;

public sealed record PostProcessingSettings
{
    public string TranscriptFolder { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Vocrec2", "Metinler");
    public string SummaryFolder { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Vocrec2", "Ozetler");
    public string LlmUrl { get; init; } = "";
    public string ProtectedLlmBearerToken { get; init; } = "";
    public string LlmModel { get; init; } = "";
    public string LlmPrompt { get; init; } = "Aşağıdaki konuşmayı Türkçe özetle. Önemli konuları, kararları ve yapılacak işleri belirt. Metinde bulunmayan bilgi ekleme.";
    public bool EnhanceMicrophone { get; init; } = true;

    public Uri? Endpoint()
    {
        if (string.IsNullOrWhiteSpace(LlmUrl)) return null;
        if (!Uri.TryCreate(LlmUrl.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("LLM URL geçerli bir HTTP/HTTPS adresi olmalıdır (sorgu içermemeli).");
        var path = uri.AbsolutePath.TrimEnd('/');
        if (!path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            path += path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? "/chat/completions" : "/v1/chat/completions";
        return new UriBuilder(uri) { Path = path }.Uri;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(TranscriptFolder) || string.IsNullOrWhiteSpace(SummaryFolder)) throw new ArgumentException("Metin ve özet klasörlerini seçin.");
        _ = Path.GetFullPath(TranscriptFolder); _ = Path.GetFullPath(SummaryFolder);
        if (Endpoint() != null && (string.IsNullOrWhiteSpace(LlmModel) || string.IsNullOrWhiteSpace(LlmPrompt))) throw new ArgumentException("LLM için model ve prompt doldurun.");
    }
}
