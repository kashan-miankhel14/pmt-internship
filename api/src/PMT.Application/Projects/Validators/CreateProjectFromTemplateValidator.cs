using FluentValidation;
using PMT.Application.Projects.Dtos;

namespace PMT.Application.Projects.Validators;

public sealed class CreateProjectFromTemplateValidator : AbstractValidator<CreateProjectFromTemplateRequest>
{
    private static readonly HashSet<string> ValidTypeCodes = ["SCRUM", "KANBAN", "BASIC"];
    private static readonly HashSet<string> ValidAccessLevels = ["OPEN", "RESTRICTED", "PRIVATE"];

    public CreateProjectFromTemplateValidator()
    {
        RuleFor(x => x.Key)
            .NotEmpty()
            .Matches("^[A-Z0-9]{2,10}$")
            .WithMessage("Key must be 2-10 uppercase alphanumeric characters.");

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(150);

        RuleFor(x => x.Description)
            .MaximumLength(4000);

        RuleFor(x => x.TypeCode)
            .Must(x => ValidTypeCodes.Contains(x))
            .WithMessage("TypeCode must be one of: SCRUM, KANBAN, BASIC.");

        RuleFor(x => x.AccessLevel)
            .Must(x => ValidAccessLevels.Contains(x))
            .WithMessage("AccessLevel must be one of: OPEN, RESTRICTED, PRIVATE.");

        RuleFor(x => x.LeadUserId)
            .GreaterThan(0)
            .WithMessage("LeadUserId must refer to a valid user.");
    }
}
