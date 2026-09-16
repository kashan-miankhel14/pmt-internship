using FluentValidation;
using PMT.Application.Tasks.Dtos;

namespace PMT.Application.Tasks.Validators;

public sealed class UpsertTaskValidator : AbstractValidator<UpsertTaskRequest>
{
    public UpsertTaskValidator()
    {
        RuleFor(x => x.ProjectId).GreaterThan(0);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(250);
        RuleFor(x => x.Priority).InclusiveBetween(1, 5);
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.EstimatedHours).GreaterThanOrEqualTo(0).When(x => x.EstimatedHours.HasValue);
        RuleFor(x => x.ActualHours).GreaterThanOrEqualTo(0).When(x => x.ActualHours.HasValue);
    }
}
