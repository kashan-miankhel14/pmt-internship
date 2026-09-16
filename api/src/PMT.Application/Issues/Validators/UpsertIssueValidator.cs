using FluentValidation;
using PMT.Application.Issues.Dtos;

namespace PMT.Application.Issues.Validators;

public sealed class UpsertIssueValidator : AbstractValidator<UpsertIssueRequest>
{
    public UpsertIssueValidator()
    {
        RuleFor(x => x.ProjectId).GreaterThan(0);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(250);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.Severity).IsInEnum();
        RuleFor(x => x.Status).IsInEnum();
    }
}
