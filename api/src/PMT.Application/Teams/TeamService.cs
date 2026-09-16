using FluentValidation;
using PMT.Application.Common.Interfaces;
using PMT.Application.Common.Models;
using PMT.Application.Teams.Dtos;
using PMT.Domain.Common;

namespace PMT.Application.Teams;

/// <summary>
/// Application service for managing teams and their members.
/// Validation runs through FluentValidation before any repository call; operations
/// that can fail return a <see cref="Result"/> / <see cref="Result{T}"/> so callers
/// can handle errors without relying on exceptions.
/// </summary>
public sealed class TeamService(
    ITeamRepository repository,
    ICurrentUserService currentUser,
    IValidator<UpsertTeamRequest> validator)
{
    public async Task<PagedResult<TeamDto>> ListAsync(int page, int pageSize, string? search)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        return await repository.ListAsync(page, pageSize, search);
    }

    public async Task<Result<TeamDto>> GetByIdAsync(long id)
    {
        var dto = await repository.GetByIdAsync(id);
        return dto is null
            ? Result<TeamDto>.Failure($"Team with id '{id}' was not found.")
            : Result<TeamDto>.Success(dto);
    }

    public async Task<Result<long>> CreateAsync(UpsertTeamRequest request)
    {
        var validation = await validator.ValidateAsync(request);
        if (!validation.IsValid)
            return Result<long>.Failure(validation.Errors.Select(x => x.ErrorMessage).ToArray());

        var createdBy = currentUser.UserId ?? 0;
        var id = await repository.CreateAsync(request, createdBy);
        return Result<long>.Success(id);
    }

    public async Task<Result> UpdateAsync(long id, UpsertTeamRequest request)
    {
        var validation = await validator.ValidateAsync(request);
        if (!validation.IsValid)
            return Result.Failure(validation.Errors.Select(x => x.ErrorMessage).ToArray());

        var updated = await repository.UpdateAsync(id, request);
        return updated
            ? Result.Success()
            : Result.Failure($"Team with id '{id}' was not found.");
    }

    public async Task<Result> DeleteAsync(long id)
    {
        var deleted = await repository.DeleteAsync(id);
        return deleted
            ? Result.Success()
            : Result.Failure($"Team with id '{id}' was not found.");
    }

    public async Task<Result> AddMemberAsync(long teamId, long userId, string teamRole)
    {
        var addedBy = currentUser.UserId ?? 0;
        var ok = await repository.AddMemberAsync(teamId, userId, teamRole, addedBy);
        return ok
            ? Result.Success()
            : Result.Failure("Unable to add the user to the team. They may already be a member.");
    }

    public async Task<Result> RemoveMemberAsync(long teamId, long userId)
    {
        var ok = await repository.RemoveMemberAsync(teamId, userId);
        return ok
            ? Result.Success()
            : Result.Failure("Unable to remove the user from the team.");
    }

    public async Task<PagedResult<TeamMemberDto>> ListMembersAsync(long teamId, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        return await repository.ListMembersAsync(teamId, page, pageSize);
    }
}
