namespace SesKayit.Core;

public sealed record LlmProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Varsayılan";
    public string Url { get; init; } = "";
    public string Model { get; init; } = "";
    public string ProtectedBearerToken { get; init; } = "";
    public string Prompt { get; init; } = new PostProcessingSettings().LlmPrompt;

    public PostProcessingSettings ApplyTo(PostProcessingSettings settings) => settings with
    {
        LlmUrl = Url, LlmModel = Model, ProtectedLlmBearerToken = ProtectedBearerToken, LlmPrompt = Prompt
    };
    public static LlmProfile FromSettings(PostProcessingSettings settings, string name, string? id = null) => new()
    {
        Id = id ?? Guid.NewGuid().ToString("N"), Name = name,
        Url = settings.LlmUrl, Model = settings.LlmModel,
        ProtectedBearerToken = settings.ProtectedLlmBearerToken, Prompt = settings.LlmPrompt
    };
    public override string ToString() => Name;
}
