using PMT.Application.Projects.Dtos;

namespace PMT.Application.Security;

/// <summary>
/// Evaluates whether a user holds a specific project-scoped permission and resolves
/// the effective role a user has on a project (direct membership and team grants combined).
/// </summary>
public interface IPermissionEvaluator
{
    Task<bool> HasProjectPermissionAsync(long userId, long projectId, string permissionKey);
    Task<EffectiveRoleDto?> GetEffectiveRoleAsync(long projectId, long userId);
}
