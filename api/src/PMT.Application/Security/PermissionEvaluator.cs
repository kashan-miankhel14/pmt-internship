using PMT.Application.Common.Interfaces;
using PMT.Application.Projects;
using PMT.Application.Projects.Dtos;

namespace PMT.Application.Security;

/// <summary>
/// Default <see cref="IPermissionEvaluator"/> implementation. The effective role is
/// resolved through <see cref="IProjectAccessRepository"/>; a permission is granted
/// when the effective role's <see cref="EffectiveRoleDto.SortOrder"/> is at or below
/// the threshold required for that permission key.
///
/// Lower <c>SortOrder</c> values represent more privileged roles. Replace the static
/// <see cref="RequiredSortOrder"/> matrix with persisted configuration if role/permission
/// mapping needs to be data-driven.
/// </summary>
public sealed class PermissionEvaluator(IProjectAccessRepository accessRepository) : IPermissionEvaluator
{
    private static readonly IReadOnlyDictionary<string, int> RequiredSortOrder = new Dictionary<string, int>
    {
        ["projects.view"] = 100,
        ["projects.manage"] = 10,
        ["stories.view"] = 100,
        ["stories.manage"] = 10,
        ["tasks.view"] = 100,
        ["tasks.manage"] = 10,
        ["issues.view"] = 100,
        ["issues.manage"] = 10,
        ["comments.manage"] = 50,
        ["attachments.manage"] = 50,
    };

    public async Task<bool> HasProjectPermissionAsync(long userId, long projectId, string permissionKey)
    {
        var role = await accessRepository.GetEffectiveRoleAsync(projectId, userId);
        if (role is null)
            return false;

        return RequiredSortOrder.TryGetValue(permissionKey, out var required)
            && role.SortOrder <= required;
    }

    public Task<EffectiveRoleDto?> GetEffectiveRoleAsync(long projectId, long userId)
        => accessRepository.GetEffectiveRoleAsync(projectId, userId);
}
