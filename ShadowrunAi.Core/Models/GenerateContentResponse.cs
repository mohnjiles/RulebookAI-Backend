using System.Text.Json.Serialization;

namespace ShadowrunAi.Core.Models;

public record GenerateContentResponse
{
    [JsonPropertyName("candidates")]
    public IList<GenerateCandidate> Candidates { get; init; } = new List<GenerateCandidate>();

    [JsonPropertyName("cachedContent")]
    public string? CachedContent { get; init; }
}

public record GenerateCandidate
{
    [JsonPropertyName("content")]
    public GenerateContent? Content { get; init; }
}

