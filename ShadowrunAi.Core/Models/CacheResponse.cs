using System.Text.Json.Serialization;

namespace ShadowrunAi.Core.Models;

public record CacheResponse
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("ttl")] 
    public string? Ttl { get; init; }

    [JsonPropertyName("expireTime")]
    public DateTimeOffset? ExpireTime { get; init; }
}

public record CreateCacheRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    [JsonPropertyName("contents")]
    public IList<GenerateContent>? Contents { get; init; }

    [JsonPropertyName("systemInstruction")]
    public GenerateSystemInstruction? SystemInstruction { get; init; }
}

public record UpdateCacheRequest
{
    [JsonPropertyName("config")]
    public required UpdateCacheConfig Config { get; init; }
}

public record UpdateCacheConfig
{
    [JsonPropertyName("ttl")]
    public required string Ttl { get; init; }
}

