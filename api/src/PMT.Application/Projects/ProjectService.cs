using FluentValidation;
using PMT.Application.Common.Interfaces;
using PMT.Application.Common.Models;
using PMT.Application.Projects.Dtos;
using PMT.Domain.Entities;
using PMT.Domain.Exceptions;
using PMT.Domain.Enums;

namespace PMT.Application.Projects;

public sealed class ProjectService(
    IProjectRepository repository,
    IValidator<UpsertProjectRequest> validator,
    IValidator<CreateProjectFromTemplateRequest> templateValidator,
    ICurrentUserService currentUser)
{
    public async Task<PagedResult<ProjectDto>> GetPagedAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var result = await repository.GetPagedAsync(page, pageSize, search, cancellationToken);
        return new PagedResult<ProjectDto>(result.Items.Select(Map).ToArray(), result.Page, result.PageSize, result.TotalCount);
    }

    public async Task<ProjectDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => Map(await repository.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException(nameof(Project), id));

    public async Task<long> CreateAsync(UpsertProjectRequest request, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(request, cancellationToken);
        var entity = new Project { InsertedBy = currentUser.UserId };
        Apply(entity, request);
        return await repository.CreateAsync(entity, cancellationToken);
    }

    /// <summary>
    /// Canonical project-creation path used by the wizard: validates the template request, uses the
    /// current user as CreatedBy and delegates to usp_Project_CreateFromTemplate (which seeds the
    /// counter, grants the lead Project Admin and audits). Returns the new project id.
    /// </summary>
    public async Task<long> CreateFromTemplateAsync(CreateProjectFromTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var result = await templateValidator.ValidateAsync(request, cancellationToken);
        if (!result.IsValid)
            throw new PMT.Domain.Exceptions.ValidationException(result.Errors.Select(x => x.ErrorMessage));

        var createdBy = currentUser.UserId ?? throw new UnauthorizedAccessException();
        return await repository.CreateFromTemplateAsync(
            request.Key.Trim().ToUpperInvariant(),
            request.Name.Trim(),
            request.Description?.Trim(),
            request.TypeCode.Trim().ToUpperInvariant(),
            request.AccessLevel.Trim().ToUpperInvariant(),
            request.LeadUserId,
            createdBy,
            cancellationToken);
    }

    public async Task UpdateAsync(long id, UpsertProjectRequest request, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(request, cancellationToken);
        var entity = await repository.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException(nameof(Project), id);
        Apply(entity, request);
        entity.UpdateDate = DateTime.UtcNow;
        entity.UpdatedBy = currentUser.UserId;
        if (!await repository.UpdateAsync(entity, cancellationToken))
            throw new NotFoundException(nameof(Project), id);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        if (!await repository.DeleteAsync(id, currentUser.UserId, cancellationToken))
            throw new NotFoundException(nameof(Project), id);
    }

    private async Task ValidateAsync(UpsertProjectRequest request, CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(request, cancellationToken);
        if (!result.IsValid)
            throw new PMT.Domain.Exceptions.ValidationException(result.Errors.Select(x => x.ErrorMessage));
    }

    private static void Apply(Project entity, UpsertProjectRequest request)
    {
        entity.Key = request.Key.Trim().ToUpperInvariant();
        entity.Name = request.Name.Trim();
        entity.Description = request.Description?.Trim();
        entity.OwnerUserId = request.OwnerUserId ?? throw new PMT.Domain.Exceptions.ValidationException(["OwnerUserId is required."]);
        entity.DepartmentId = request.DepartmentId ?? throw new PMT.Domain.Exceptions.ValidationException(["DepartmentId is required."]);
        entity.Status = request.Status;
        entity.StartDate = request.StartDate;
        entity.TargetDate = request.TargetDate;
        entity.Active = request.Active;
    }

    private static ProjectDto Map(Project x) => new(x.Id, x.Key, x.Name, x.Description, x.OwnerUserId, x.Status, x.StartDate, x.TargetDate, x.Active, x.DepartmentId);
}
