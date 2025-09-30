namespace ShadowrunAi.Core.Models;

public record ChatSession
{
    public Guid Id { get; init; }

    public string? AccountId { get; set; }

    public string Title { get; set; } = "New chat";

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? ProviderCacheId { get; set; }

    public string? FileUri { get; set; }

    public string? FileName { get; set; }

    public string? MimeType { get; set; }

    public string? ProviderFileId { get; set; }

    public string? StorageBlobName { get; set; }

    public string? SystemInstruction { get; set; }

    public DateTimeOffset? CacheExpiresAt { get; set; }

    public DateTimeOffset? FileExpiresAt { get; set; }

    public string? ProviderCacheDisplayName { get; set; }

    public List<ChatTurn> Turns { get; init; } = new();
}

