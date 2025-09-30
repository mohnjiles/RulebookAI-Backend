namespace ShadowrunAi.Core.Models;

public record UploadResult(
    string ProviderId,
    string DisplayName,
    DateTimeOffset UploadedAt,
    string? CachedContentName = null,
    string? FileUri = null,
    string? MimeType = null,
    string? SystemInstruction = null,
    DateTimeOffset? ExpirationTime = null);

