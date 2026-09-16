using FluentValidation;
using PMT.Application.Departments.Dtos;

namespace PMT.Application.Departments.Validators;

public sealed class UpsertDepartmentValidator : AbstractValidator<UpsertDepartmentRequest>
{
    public UpsertDepartmentValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20).Matches("^[A-Za-z0-9_-]+$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}
