using PMT.Application.Common.Interfaces;
using PMT.Application.Common.Models;
using PMT.Application.Projects.Dtos;
using PMT.Domain.Common;

namespace PMT.Application.Projects;

/// <summary>
/// Application service for managing project membership (direct user members and
/// team grants) and resolving the effective role of the current user on a project.
/// </summary>
public sealed class ProjectAccessService(
    IProjectAccessRepository repository,
    ICurrentUserService currentUser)
{
    /// <summary>
    /// Resolves the project key carried in the route to the surrogate id the rest of this
    /// service works with. Returns <c>null</c> when the key matches no live project, which
    /// callers surface as a 404.
    /// </summary>
    public Task<long?> ResolveProjectIdAsync(string projectKey)
        => repository.GetProjectIdByKeyAsync(projectKey);

    public async Task<PagedResult<ProjectMemberDto>> ListMembersAsync(long projectId, int page, int pageSize, string? search)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        return await repository.ListMembersAsync(projectId, page, pageSize, search);
    }

    public async Task<Result> AddMemberAsync(long projectId, long userId, long projectRoleId)
    {
        var addedBy = currentUser.UserId ?? 0;
        var ok = await repository.AddMemberAsync(projectId, userId, projectRoleId, addedBy);
        return ok
            ? Result.Success()
            : Result.Failure("Unable to add the member to the project. They may already be a member.");
    }

    public async Task<Result> RemoveMemberAsync(long projectId, long userId)
    {
        var ok = await repository.RemoveMemberAsync(projectId, userId);
        return ok
            ? Result.Success()
            : Result.Failure("Unable to remove the member from the project.");
    }

    public async Task<PagedResult<ProjectTeamDto>> ListTeamGrantsAsync(long projectId, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        return await repository.ListTeamGrantsAsync(projectId, page, pageSize);
    }

    public async Task<Result> AddTeamGrantAsync(long projectId, long teamId, long projectRoleId)
    {
        var addedBy = currentUser.UserId ?? 0;
        var ok = await repository.AddTeamGrantAsync(projectId, teamId, projectRoleId, addedBy);
        return ok
            ? Result.Success()
            : Result.Failure("Unable to grant the team access to the project.");
    }

    public async Task<Result> RemoveTeamGrantAsync(long projectId, long teamId)
    {
        var ok = await repository.RemoveTeamGrantAsync(projectId, teamId);
        return ok
            ? Result.Success()
            : Result.Failure("Unable to remove the team grant from the project.");
    }

    /// <summary>Resolves the effective role of the currently authenticated user on the project.</summary>
    public async Task<Result<EffectiveRoleDto?>> GetMyRoleAsync(long projectId)
    {
        var userId = currentUser.UserId;
        if (userId is null)
            return Result<EffectiveRoleDto?>.Failure("No authenticated user.");

        var role = await repository.GetEffectiveRoleAsync(projectId, userId.Value);
        return Result<EffectiveRoleDto?>.Success(role);
    }
}
