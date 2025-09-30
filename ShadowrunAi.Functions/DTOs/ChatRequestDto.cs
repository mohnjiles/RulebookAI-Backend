using FluentValidation;

namespace ShadowrunAi.Functions.DTOs;

public record ChatRequestDto(Guid SessionId, string Message);

public record RerunRequestDto(Guid SessionId, int TurnIndex, string? UserMessage);

public record DeleteMessageRequestDto(Guid SessionId, int TurnIndex, string Role);

public class ChatRequestDtoValidator : AbstractValidator<ChatRequestDto>
{
    public ChatRequestDtoValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.Message).NotEmpty().MaximumLength(2000);
    }
}

public class RerunRequestDtoValidator : AbstractValidator<RerunRequestDto>
{
    public RerunRequestDtoValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.TurnIndex).GreaterThanOrEqualTo(0);
        // UserMessage is optional if the referenced turn has a user message
    }
}

public class DeleteMessageRequestDtoValidator : AbstractValidator<DeleteMessageRequestDto>
{
    public DeleteMessageRequestDtoValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.TurnIndex).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Role).NotEmpty().Must(r => r == "user" || r == "ai")
            .WithMessage("Role must be 'user' or 'ai'");
    }
}

