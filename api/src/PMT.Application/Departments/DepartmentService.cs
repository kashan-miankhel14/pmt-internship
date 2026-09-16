using FluentValidation;
using PMT.Application.Common.Caching;
using PMT.Application.Common.Interfaces;
using PMT.Application.Common.Models;
using PMT.Application.Departments.Dtos;
using PMT.Domain.Entities;
using PMT.Domain.Exceptions;


namespace PMT.Application.Departments;

public sealed class DepartmentService(
    IDepartmentRepository repository,
    IValidator<UpsertDepartmentRequest> validator,
    ICurrentUserService currentUser,
    ILookupCache cache)
{
    public Task<PagedResult<DepartmentDto>> GetPagedAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        // Free-text searches are not cached: the key space is unbounded and the hit rate is
        // low. The unfiltered pages are the ones every CRUD screen loads, so those are cached.
        if (!string.IsNullOrWhiteSpace(search))
            return LoadPageAsync(page, pageSize, search, cancellationToken);

        return cache.GetOrCreateAsync(
            CacheRegions.Departments,
            $"paged:{page}:{pageSize}",
            CacheRegions.LookupTtl,
            ct => LoadPageAsync(page, pageSize, null, ct),
            cancellationToken);
    }

    private async Task<PagedResult<DepartmentDto>> LoadPageAsync(int page, int pageSize, string? search, CancellationToken cancellationToken)
    {
        var result = await repository.GetPagedAsync(page, pageSize, search, cancellationToken);
        return new PagedResult<DepartmentDto>(result.Items.Select(Map).ToArray(), result.Page, result.PageSize, result.TotalCount);
    }

    public Task<DepartmentDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => cache.GetOrCreateAsync(
            CacheRegions.Departments,
            $"id:{id}",
            CacheRegions.LookupTtl,
            async ct => Map(await repository.GetByIdAsync(id, ct) ?? throw new NotFoundException(nameof(Department), id)),
            cancellationToken);

    public async Task<long> CreateAsync(UpsertDepartmentRequest request, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(request, cancellationToken);
        var entity = new Department { InsertedBy = currentUser.UserId };
        Apply(entity, request);
        var id = await repository.CreateAsync(entity, cancellationToken);
        cache.Invalidate(CacheRegions.Departments);
        return id;
    }

    public async Task UpdateAsync(long id, UpsertDepartmentRequest request, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(request, cancellationToken);
        // Reads the entity straight from the repository: the cache holds mapped DTOs on the
        // read path only, so it can never feed a stale entity into a write.
        var entity = await repository.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException(nameof(Department), id);
        Apply(entity, request);
        entity.UpdateDate = DateTime.UtcNow;
        entity.UpdatedBy = currentUser.UserId;
        if (!await repository.UpdateAsync(entity, cancellationToken))
            throw new NotFoundException(nameof(Department), id);
        cache.Invalidate(CacheRegions.Departments);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        if (!await repository.DeleteAsync(id, currentUser.UserId, cancellationToken))
            throw new NotFoundException(nameof(Department), id);
        cache.Invalidate(CacheRegions.Departments);
    }

    private async Task ValidateAsync(UpsertDepartmentRequest request, CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(request, cancellationToken);
        if (!result.IsValid)
            throw new PMT.Domain.Exceptions.ValidationException(result.Errors.Select(x => x.ErrorMessage));
    }

    private static void Apply(Department entity, UpsertDepartmentRequest request)
    {
        entity.Code = request.Code.Trim().ToUpperInvariant();
        entity.Name = request.Name.Trim();
        entity.Description = request.Description?.Trim();
        entity.Active = request.Active;
    }

    private static DepartmentDto Map(Department x) => new(x.Id, x.Code, x.Name, x.Description, x.Active);
}
