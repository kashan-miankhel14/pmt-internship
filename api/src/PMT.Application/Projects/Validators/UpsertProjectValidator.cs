using FluentValidation;
using PMT.Application.Projects.Dtos;

namespace PMT.Application.Projects.Validators;

public sealed class UpsertProjectValidator : AbstractValidator<UpsertProjectRequest>
{
    public UpsertProjectValidator()
    {
        RuleFor(x => x.Key).NotEmpty().MaximumLength(15).Matches("^[A-Za-z][A-Za-z0-9_-]*$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x).Must(x => !x.StartDate.HasValue || !x.TargetDate.HasValue || x.TargetDate.Value >= x.StartDate.Value)
            .WithMessage("Target date must be on or after the start date.");
    }
}
