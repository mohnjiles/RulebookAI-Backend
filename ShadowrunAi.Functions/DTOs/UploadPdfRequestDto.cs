using FluentValidation;

namespace ShadowrunAi.Functions.DTOs;

public record UploadPdfRequestDto(Guid SessionId, string FileName, Stream Content, string? SystemInstruction);

public class UploadPdfRequestDtoValidator : AbstractValidator<UploadPdfRequestDto>
{
    public UploadPdfRequestDtoValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.FileName).NotEmpty();
        RuleFor(x => x.Content).NotNull();
    }
}

