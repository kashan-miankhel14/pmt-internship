using FluentValidation;
using PMT.Application.Boards.Dtos;

namespace PMT.Application.Boards.Validators;

public sealed class UpsertBoardColumnValidator : AbstractValidator<UpsertBoardColumnRequest>
{
    public UpsertBoardColumnValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Ordinal).GreaterThanOrEqualTo(0);
    }
}
