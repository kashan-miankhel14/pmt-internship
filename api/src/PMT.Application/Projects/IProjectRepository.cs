using PMT.Application.Common.Models;
using PMT.Domain.Entities;

namespace PMT.Application.Projects;

public interface IProjectRepository
{
    Task<PagedResult<Project>> GetPagedAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<Project?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<long> CreateAsync(Project entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a project through dbo.usp_Project_CreateFromTemplate, which atomically inserts the
    /// project, seeds its ProjectCounters row, grants the lead (and creator) Project Admin and
    /// writes the audit entry. Returns the new project id from the procedure's OUTPUT parameter.
    /// </summary>
    Task<long> CreateFromTemplateAsync(
        string key,
        string name,
        string? description,
        string typeCode,
        string accessLevel,
        long leadUserId,
        long createdBy,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateAsync(Project entity, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(long id, long? deletedBy, CancellationToken cancellationToken = default);
}
