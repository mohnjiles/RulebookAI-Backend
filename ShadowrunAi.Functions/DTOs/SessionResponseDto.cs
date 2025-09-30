namespace ShadowrunAi.Functions.DTOs;

public record SessionResponseDto
{
    public Guid Id { get; init; }

    public string Title { get; init; } = "New chat";

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public int MessageCount { get; init; }
}

