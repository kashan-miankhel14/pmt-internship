using FluentValidation;
using PMT.Application.Teams.Dtos;

namespace PMT.Application.Teams.Validators;

public sealed class UpsertTeamValidator : AbstractValidator<UpsertTeamRequest>
{
    public UpsertTeamValidator()
    {
        RuleFor(x => x.Key)
            .NotEmpty()
            .MaximumLength(10)
            .Matches("^[A-Z][A-Z0-9]{1,9}$")
            .WithMessage("Key must be 2-10 uppercase alphanumeric characters and start with a letter (e.g. ENG).");

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(150);

        RuleFor(x => x.LeadUserId)
            .GreaterThan(0)
            .WithMessage("LeadUserId must refer to a valid user.");
    }
}
