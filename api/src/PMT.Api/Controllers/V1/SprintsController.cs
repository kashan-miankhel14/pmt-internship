using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PMT.Application.Common.Models;
using PMT.Application.Common.Security;
using PMT.Application.Sprints;
using PMT.Application.Sprints.Dtos;
using PMT.Domain.Common;

namespace PMT.Api.Controllers.V1;

/// <summary>
/// Sprint planning for a project addressed by its public key (for example <c>PMT</c>)
/// rather than its surrogate id, matching the other project-scoped controllers.
/// </summary>
[Authorize(Policy = PermissionRequirement.ProjectsView)]
[ApiController, Route("api/v1/projects/{projectKey}/sprints"), EnableRateLimiting("api")]
public sealed class SprintsController(SprintService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<SprintDto>>> ListAsync(
        string projectKey,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var projectId = await service.ResolveProjectIdAsync(projectKey);
        if (projectId is null)
            return ProjectNotFound(projectKey);

        return Ok(await service.GetPagedAsync(projectId.Value, page, pageSize, search, cancellationToken));
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<SprintDto>> GetAsync(string projectKey, long id, CancellationToken cancellationToken)
    {
        var projectId = await service.ResolveProjectIdAsync(projectKey);
        if (projectId is null)
            return ProjectNotFound(projectKey);

        var result = await service.GetByIdAsync(projectId.Value, id, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : NotFound(ErrorPayload(result));
    }

    [Authorize(Policy = PermissionRequirement.ProjectsManage)]
    [HttpPost]
    public async Task<ActionResult> CreateAsync(string projectKey, UpsertSprintRequest request, CancellationToken cancellationToken)
    {
        var projectId = await service.ResolveProjectIdAsync(projectKey);
        if (projectId is null)
            return ProjectNotFound(projectKey);

        var result = await service.CreateAsync(projectId.Value, request, cancellationToken);
        if (!result.Succeeded)
            return BadRequest(ErrorPayload(result));

        var id = result.Value;
        // MVC trims the "Async" suffix when it derives action names, so the target of this
        // link is the action "Get", not "GetAsync".
        return CreatedAtAction("Get", new { projectKey, id }, new { id });
    }

    [Authorize(Policy = PermissionRequirement.ProjectsManage)]
    [HttpPut("{id:long}")]
    public async Task<ActionResult> UpdateAsync(string projectKey, long id, UpsertSprintRequest request, CancellationToken cancellationToken)
    {
        var projectId = await service.ResolveProjectIdAsync(projectKey);
        if (projectId is null)
            return ProjectNotFound(projectKey);

        return Resolve(await service.UpdateAsync(projectId.Value, id, request, cancellationToken));
    }

    [Authorize(Policy = PermissionRequirement.ProjectsManage)]
    [HttpDelete("{id:long}")]
    public async Task<ActionResult> DeleteAsync(string projectKey, long id, CancellationToken cancellationToken)
    {
        var projectId = await service.ResolveProjectIdAsync(projectKey);
        if (projectId is null)
            return ProjectNotFound(projectKey);

        return Resolve(await service.DeleteAsync(projectId.Value, id, cancellationToken));
    }

    /// <summary>
    /// Starts a planned sprint. Only a PLANNED sprint can be started; anything else is
    /// rejected rather than silently accepted. The action carries no payload.
    /// </summary>
    [Authorize(Policy = PermissionRequirement.ProjectsManage)]
    [HttpPost("{id:long}/start")]
    public async Task<ActionResult> StartAsync(string projectKey, long id, CancellationToken cancellationToken)
    {
        var projectId = await service.ResolveProjectIdAsync(projectKey);
        if (projectId is null)
            return ProjectNotFound(projectKey);

        return Resolve(await service.StartAsync(projectId.Value, id, cancellationToken));
    }

    /// <summary>
    /// Closes an active sprint. Only an ACTIVE sprint can be completed; anything else is
    /// rejected rather than silently accepted.
    /// </summary>
    [Authorize(Policy = PermissionRequirement.ProjectsManage)]
    [HttpPost("{id:long}/complete")]
    public async Task<ActionResult> CompleteAsync(string projectKey, long id, CancellationToken cancellationToken)
    {
        var projectId = await service.ResolveProjectIdAsync(projectKey);
        if (projectId is null)
            return ProjectNotFound(projectKey);

        return Resolve(await service.CompleteAsync(projectId.Value, id, cancellationToken));
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
