using System.Text.Json.Serialization;

namespace ShadowrunAi.Core.Models;

public record GenerateContentRequest
{
    [JsonPropertyName("contents")]
    public required IList<GenerateContent> Contents { get; init; }

    [JsonPropertyName("systemInstruction")]
    public GenerateSystemInstruction? SystemInstruction { get; init; }

    [JsonPropertyName("cachedContent")]
    public string? CachedContent { get; init; }
}

public record GenerateContent
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("parts")]
    public required IList<GeneratePart> Parts { get; init; }
}

public record GeneratePart
{
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("fileData")]
    public FilePartData? FileData { get; init; }
}

public record FilePartData
{
    [JsonPropertyName("mimeType")]
    public required string MimeType { get; init; }

    [JsonPropertyName("fileUri")]
    public required string FileUri { get; init; }
}

public record GenerateConfig
{
    [JsonPropertyName("cachedContent")]
    public string? CachedContent { get; init; }

    [JsonPropertyName("systemInstruction")]
    public GenerateSystemInstruction? SystemInstruction { get; init; }
}

public record GenerateSystemInstruction
{
    [JsonPropertyName("parts")]
    public required IList<GeneratePart> Parts { get; init; }
}

