using FluentValidation;
using PMT.Application.Users.Dtos;

namespace PMT.Application.Users.Validators;

public sealed class UpsertUserValidator : AbstractValidator<UpsertUserRequest>
{
    public UpsertUserValidator()
    {
        RuleFor(x => x.UserName).NotEmpty().MinimumLength(3).MaximumLength(50);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Password).MinimumLength(10).When(x => !string.IsNullOrWhiteSpace(x.Password));
    }
}
