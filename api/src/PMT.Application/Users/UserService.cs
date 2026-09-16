using FluentValidation;
using PMT.Application.Common.Caching;
using PMT.Application.Common.Interfaces;
using PMT.Application.Common.Models;
using PMT.Application.Users.Dtos;
using PMT.Domain.Entities;
using PMT.Domain.Exceptions;


namespace PMT.Application.Users;

public sealed class UserService(
    IUserRepository repository,
    IValidator<UpsertUserRequest> validator,
    ICurrentUserService currentUser, IPasswordHasher passwordHasher,
    ILookupCache cache)
{
    public Task<PagedResult<UserDto>> GetPagedAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        // Searches bypass the cache; see DepartmentService.GetPagedAsync for the rationale.
        if (!string.IsNullOrWhiteSpace(search))
            return LoadPageAsync(page, pageSize, search, cancellationToken);

        return cache.GetOrCreateAsync(
            CacheRegions.Users,
            $"paged:{page}:{pageSize}",
            CacheRegions.LookupTtl,
            ct => LoadPageAsync(page, pageSize, null, ct),
            cancellationToken);
    }

    private async Task<PagedResult<UserDto>> LoadPageAsync(int page, int pageSize, string? search, CancellationToken cancellationToken)
    {
        var result = await repository.GetPagedAsync(page, pageSize, search, cancellationToken);
        return new PagedResult<UserDto>(result.Items.Select(Map).ToArray(), result.Page, result.PageSize, result.TotalCount);
    }

    public Task<UserDto> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => cache.GetOrCreateAsync(
            CacheRegions.Users,
            $"id:{id}",
            CacheRegions.LookupTtl,
            async ct => Map(await repository.GetByIdAsync(id, ct) ?? throw new NotFoundException(nameof(User), id)),
            cancellationToken);

    public async Task<long> CreateAsync(UpsertUserRequest request, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(request, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Password))
            throw new PMT.Domain.Exceptions.ValidationException(["Password is required when creating a user."]);

        var entity = new User { InsertedBy = currentUser.UserId };
        Apply(entity, request);
        entity.PasswordHash = passwordHasher.Hash(request.Password);

        var id = await repository.CreateAsync(entity, cancellationToken);
        cache.Invalidate(CacheRegions.Users);
        return id;
    }

    public async Task UpdatePasswordAsync(long id, string newPassword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword))
            throw new PMT.Domain.Exceptions.ValidationException(["New password is required."]);
        if (newPassword.Length < 6)
            throw new PMT.Domain.Exceptions.ValidationException(["Password must be at least 6 characters long."]);

        var hash = passwordHasher.Hash(newPassword);
        var updated = await repository.UpdatePasswordAsync(id, hash, currentUser.UserId, cancellationToken);
        if (!updated) throw new NotFoundException(nameof(User), id);
        cache.Invalidate(CacheRegions.Users);
    }

    public async Task UpdateAsync(long id, UpsertUserRequest request, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(request, cancellationToken);
        // Straight from the repository, not the cache: the cache only stores read-path DTOs.
        var entity = await repository.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException(nameof(User), id);
        Apply(entity, request);
        if (!string.IsNullOrWhiteSpace(request.Password))
            entity.PasswordHash = passwordHasher.Hash(request.Password);

        entity.UpdateDate = DateTime.UtcNow;
        entity.UpdatedBy = currentUser.UserId;
        if (!await repository.UpdateAsync(entity, cancellationToken))
            throw new NotFoundException(nameof(User), id);
        cache.Invalidate(CacheRegions.Users);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        if (!await repository.DeleteAsync(id, currentUser.UserId, cancellationToken))
            throw new NotFoundException(nameof(User), id);
        cache.Invalidate(CacheRegions.Users);
    }

    public async Task ResetPasswordAsync(long id, ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword))
            throw new PMT.Domain.Exceptions.ValidationException(["New password is required."]);
        if (request.NewPassword.Length < 6)
            throw new PMT.Domain.Exceptions.ValidationException(["Password must be at least 6 characters long."]);

        var hash = passwordHasher.Hash(request.NewPassword);
        if (!await repository.UpdatePasswordAsync(id, hash, currentUser.UserId, cancellationToken))
            throw new NotFoundException(nameof(User), id);
        cache.Invalidate(CacheRegions.Users);
    }

    /// <summary>
    /// The assignable-role list is reference data read by every user create/edit screen and
    /// mutated by no endpoint, so it gets the longest TTL of the lookups.
    /// </summary>
    public Task<IReadOnlyCollection<RoleDto>> GetAvailableRolesAsync(CancellationToken cancellationToken = default)
        => cache.GetOrCreateAsync(
            CacheRegions.Roles,
            "available",
            CacheRegions.RolesTtl,
            async ct =>
            {
                var roles = await repository.GetAvailableRolesAsync(ct);
                return (IReadOnlyCollection<RoleDto>)roles.Select(MapRole).ToArray();
            },
            cancellationToken);

    public async Task<IReadOnlyCollection<RoleDto>> GetRolesAsync(long userId, CancellationToken cancellationToken = default)
    {
        _ = await repository.GetByIdAsync(userId, cancellationToken) ?? throw new NotFoundException(nameof(User), userId);
        return (await repository.GetUserRolesAsync(userId, cancellationToken)).Select(MapRole).ToArray();
    }

    public async Task SetRolesAsync(long userId, SetUserRolesRequest request, CancellationToken cancellationToken = default)
    {
        if (request.RoleIds is null || request.RoleIds.Any(x => x <= 0))
            throw new PMT.Domain.Exceptions.ValidationException(["Role IDs must be positive values."]);
        if (!await repository.SetUserRolesAsync(userId, request.RoleIds.Distinct().ToArray(), currentUser.UserId, cancellationToken))
            throw new NotFoundException(nameof(User), userId);
        // UserDto carries RoleId, so a role change makes every cached user projection stale.
        cache.Invalidate(CacheRegions.Users);
    }

    private async Task ValidateAsync(UpsertUserRequest request, CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(request, cancellationToken);
        if (!result.IsValid)
            throw new PMT.Domain.Exceptions.ValidationException(result.Errors.Select(x => x.ErrorMessage));
    }

    private static void Apply(User entity, UpsertUserRequest request)
    {
        entity.DepartmentId = request.DepartmentId ?? throw new PMT.Domain.Exceptions.ValidationException(["DepartmentId is required."]);
        entity.RoleId = request.RoleId ?? (entity.RoleId > 0 ? entity.RoleId : throw new PMT.Domain.Exceptions.ValidationException(["RoleId is required."]));
        entity.UserName = request.UserName.Trim();
        entity.Email = request.Email.Trim().ToLowerInvariant();
        entity.DisplayName = request.DisplayName.Trim();
        entity.Active = request.Active;
    }

    private static UserDto Map(User x) => new(x.Id, x.DepartmentId, x.UserName, x.Email, x.DisplayName, x.IsLocked, x.Active, x.RoleId);
    private static RoleDto MapRole(Role x) => new(x.Id, x.Name, x.Description, x.IsSystemRole);
}
