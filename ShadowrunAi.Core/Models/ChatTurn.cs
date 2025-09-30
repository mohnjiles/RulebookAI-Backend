namespace ShadowrunAi.Core.Models;

public record ChatTurn
{
    public ChatMessage? User { get; set; }

    public ChatMessage? Assistant { get; set; }

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

