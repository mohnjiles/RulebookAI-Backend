namespace ShadowrunAi.Core.Models;

public record ChatMessage
{
    public required string Role { get; init; }

    public required string Text { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

