namespace ShadowrunAi.Core.Models;

public record ChatSessionSummary
{
    public Guid Id { get; init; }

    public string Title { get; init; } = "New chat";

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    public int MessageCount { get; init; }

    public bool HasCachedPdf => !string.IsNullOrEmpty(ProviderCacheId) || !string.IsNullOrEmpty(ProviderFileId);

    public string? ProviderCacheId { get; init; }

    public string? ProviderFileId { get; init; }
}

