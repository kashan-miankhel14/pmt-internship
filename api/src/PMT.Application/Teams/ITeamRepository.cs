using PMT.Application.Common.Models;
using PMT.Application.Teams.Dtos;

namespace PMT.Application.Teams;

public interface ITeamRepository
{
    Task<PagedResult<TeamDto>> ListAsync(int page, int pageSize, string? search);
    Task<TeamDto?> GetByIdAsync(long id);
    Task<long> CreateAsync(UpsertTeamRequest request, long createdBy);
    Task<bool> UpdateAsync(long id, UpsertTeamRequest request);
    Task<bool> DeleteAsync(long id);

    // Members
    Task<bool> AddMemberAsync(long teamId, long userId, string teamRole, long addedBy);
    Task<bool> RemoveMemberAsync(long teamId, long userId);
    Task<PagedResult<TeamMemberDto>> ListMembersAsync(long teamId, int page, int pageSize);
}
