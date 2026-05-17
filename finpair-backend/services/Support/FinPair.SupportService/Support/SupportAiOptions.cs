namespace FinPair.SupportService.Support;

public sealed class SupportAiOptions
{
    public string BaseUrl { get; init; } = "https://ollama.com";

    public string Model { get; init; } = "gpt-oss:120b";

    public string? ApiKey { get; init; }

    public int TimeoutSeconds { get; init; } = 90;
}
