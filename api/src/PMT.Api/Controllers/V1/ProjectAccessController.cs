using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PMT.Application.Common.Models;
using PMT.Application.Common.Security;
using PMT.Application.Projects;
using PMT.Application.Projects.Dtos;
using PMT.Domain.Common;

namespace PMT.Api.Controllers.V1;

/// <summary>
/// Membership and access management for a project addressed by its public key
/// (for example <c>PMT</c>) rather than its surrogate id.
/// </summary>
[Authorize(Policy = PermissionRequirement.ProjectsView)]
[ApiController, Route("api/v1/projects/{projectKey}"), EnableRateLimiting("api")]
public sealed class ProjectAccessController(ProjectAccessService service) : ControllerBase
{
    [HttpGet("members")]
    public async Task<ActionResult<PagedResult<ProjectMemberDto>>> ListMembersAsync(
        string projectKey,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null)
    {
        var projectId = await service.ResolveProjectIdAsync(projectKey);
        if (projectId is null)
            return ProjectNotFound(projectKey);

        return Ok(await service.ListMembersAsync(projectId.Value, page, pageSize, search));
    }

    [Authorize(Policy = PermissionRequirement.ProjectsManage)]
    [HttpPost("members")]
    public async Task<ActionResult> AddMemberAsync(string projectKey, AddProjectMemberRequest request)
    {
        var projectId = await service.ResolveProjectIdAsync(projectKey);
        if (projectId is null)
            return ProjectNotFound(projectKey);

        return Resolve(await service.AddMemberAsync(projectId.Value, request.UserId, request.ProjectRoleId));
    }

    [Authorize(Policy = PermissionRequirement.ProjectsManage)]
    [HttpDelete("members/{userId:long}")]
    public async Task<ActionResult> RemoveMemberAsync(string projectKey, long userId)
    {
        var projectId = await service.ResolveProjectIdAsync(projectKey);
        if (projectId is null)
            return ProjectNotFound(projectKey);

        return Resolve(await service.RemoveMemberAsync(projectId.Value, userId));
    }

    [HttpGet("teams")]
    public async Task<ActionResult<PagedResult<ProjectTeamDto>>> ListTeamGrantsAsync(
        string projectKey,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        var projectId = await service.ResolveProjectIdAsync(projectKey);
        if (projectId is null)
            return ProjectNotFound(projectKey);

        return Ok(await service.ListTeamGrantsAsync(projectId.Value, page, pageSize));
    }

    [Authorize(Policy = PermissionRequirement.ProjectsManage)]
    [HttpPost("teams")]
    public async Task<ActionResult> AddTeamGrantAsync(string projectKey, AddProjectTeamRequest request)
    {
        var projectId = await service.ResolveProjectIdAsync(projectKey);
        if (projectId is null)
            return ProjectNotFound(projectKey);

        return Resolve(await service.AddTeamGrantAsync(projectId.Value, request.TeamId, request.ProjectRoleId));
    }

    [Authorize(Policy = PermissionRequirement.ProjectsManage)]
    [HttpDelete("teams/{teamId:long}")]
    public async Task<ActionResult> RemoveTeamGrantAsync(string projectKey, long teamId)
    {
        var projectId = await service.ResolveProjectIdAsync(projectKey);
        if (projectId is null)
            return ProjectNotFound(projectKey);

        return Resolve(await service.RemoveTeamGrantAsync(projectId.Value, teamId));
    }

    /// <summary>
    /// The caller's effective role on the project, combining direct membership and team
    /// grants. Responds with <c>null</c> when the caller holds no role, which is a valid
    /// answer rather than an error.
    /// </summary>
    [HttpGet("my-role")]
    public async Task<ActionResult<EffectiveRoleDto>> GetMyRoleAsync(string projectKey)
    {
        var projectId = await service.ResolveProjectIdAsync(projectKey);
        if (projectId is null)
            return ProjectNotFound(projectKey);

        var result = await service.GetMyRoleAsync(projectId.Value);
        return result.Succeeded ? Ok(result.Value) : Unauthorized(ErrorPayload(result));
    }

    private ActionResult ProjectNotFound(string projectKey)
        => NotFound(new { errors = new[] { $"Project with key '{projectKey}' was not found." } });

    /// <summary>
    /// Maps a write <see cref="Result"/> onto a status code. The service reports a missing
    /// row and a rejected payload through the same failure channel, so the message is
    /// inspected to tell 404 from 400.
    /// </summary>
    private ActionResult Resolve(Result result)
    {
        if (result.Succeeded)
            return NoContent();

        return result.Errors.Any(error => error.Contains("not found", StringComparison.OrdinalIgnoreCase))
            ? NotFound(ErrorPayload(result))
            : BadRequest(ErrorPayload(result));
    }

    private static object ErrorPayload(Result result) => new { errors = result.Errors };
}
