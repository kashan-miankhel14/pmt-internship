using PMT.Application.Common.Models;
using PMT.Application.Projects.Dtos;

namespace PMT.Application.Projects;

public interface IProjectAccessRepository
{
    /// <summary>
    /// Resolves the public project key used in API routes to its surrogate id, or
    /// <c>null</c> when no live project carries that key.
    /// </summary>
    Task<long?> GetProjectIdByKeyAsync(string projectKey);

    Task<PagedResult<ProjectMemberDto>> ListMembersAsync(long projectId, int page, int pageSize, string? search);
    Task<bool> AddMemberAsync(long projectId, long userId, long projectRoleId, long addedBy);
    Task<bool> RemoveMemberAsync(long projectId, long userId);

    Task<PagedResult<ProjectTeamDto>> ListTeamGrantsAsync(long projectId, int page, int pageSize);
    Task<bool> AddTeamGrantAsync(long projectId, long teamId, long projectRoleId, long addedBy);
    Task<bool> RemoveTeamGrantAsync(long projectId, long teamId);

    Task<EffectiveRoleDto?> GetEffectiveRoleAsync(long projectId, long userId);
}
