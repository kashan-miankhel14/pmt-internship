using FluentValidation;
using PMT.Application.AiAgent.Dtos;

namespace PMT.Application.AiAgent.Validators;

public sealed class ChatRequestValidator : AbstractValidator<ChatRequestDto>
{
    /// <summary>
    /// Upper bound on a single user turn. Keeps one request from consuming the whole
    /// model context window and blunts trivially oversized payloads.
    /// </summary>
    public const int MaxMessageLength = 4000;

    public ChatRequestValidator()
    {
        RuleFor(x => x.Message)
            .NotEmpty().WithMessage("Message is required.")
            .MaximumLength(MaxMessageLength)
            .WithMessage($"Message must not exceed {MaxMessageLength} characters.");

        RuleFor(x => x.ProjectId)
            .GreaterThan(0).When(x => x.ProjectId.HasValue)
            .WithMessage("ProjectId must be greater than zero.");

        RuleFor(x => x.SessionId)
            .NotEqual(Guid.Empty).When(x => x.SessionId.HasValue)
            .WithMessage("SessionId must be a valid identifier.");
    }
}
