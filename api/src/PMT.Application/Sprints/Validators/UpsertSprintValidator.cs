using FluentValidation;
using PMT.Application.Sprints.Dtos;

namespace PMT.Application.Sprints.Validators;

public sealed class UpsertSprintValidator : AbstractValidator<UpsertSprintRequest>
{
    public UpsertSprintValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Goal).MaximumLength(1000);

        // Status is nullable: null means "leave the stored status alone" (PLANNED on create),
        // and IsInEnum passes a null through, so only a supplied value has to be a member.
        RuleFor(x => x.Status).IsInEnum();

        // Only meaningful when both ends are supplied: an open-ended sprint is legitimate
        // while it is still being planned.
        RuleFor(x => x.EndDate)
            .Must((request, endDate) =>
                endDate is null || request.StartDate is null || endDate.Value >= request.StartDate.Value)
            .WithMessage("EndDate must be on or after StartDate.");
    }
}
