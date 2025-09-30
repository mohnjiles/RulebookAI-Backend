using System.Text.Json.Serialization;

namespace ShadowrunAi.Core.Models;

public record FileUploadResponse
{
    [JsonPropertyName("file")]
    public UploadedFile? File { get; init; }
}

public record UploadedFile
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }
    
    [JsonPropertyName("uri")]
    public string? Uri { get; init; }
    
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }
    
    [JsonPropertyName("state")]
    public string? State { get; init; }
    
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; init; }
    
    [JsonPropertyName("expirationTime")]
    public DateTimeOffset? ExpirationTime { get; init; }
}

public record FileStatusResponse
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("uri")]
    public string? Uri { get; init; }

    [JsonPropertyName("mimeType")]
    public string? MimeType { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }
}

