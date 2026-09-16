using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PMT.Application.Boards;
using PMT.Application.Boards.Dtos;
using PMT.Application.Common.Security;
using PMT.Domain.Common;

namespace PMT.Api.Controllers.V1;

/// <summary>
/// Board column configuration for a project addressed by its public key (for example
/// <c>PMT</c>) rather than its surrogate id. The columns are seeded from the project's
/// template at creation time and can be reshaped here.
/// </summary>
[Authorize(Policy = PermissionRequirement.ProjectsView)]
[ApiController, Route("api/v1/projects/{projectKey}/columns"), EnableRateLimiting("api")]
public sealed class BoardColumnsController(BoardColumnService service) : ControllerBase
{
    /// <summary>The project's board, in left-to-right (Ordinal) order.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<BoardColumnDto>>> ListAsync(string projectKey, CancellationToken cancellationToken)
    {
        var projectId = await service.ResolveProjectIdAsync(projectKey);
        if (projectId is null)
            return ProjectNotFound(projectKey);

        return Ok(await service.GetByProjectAsync(projectId.Value, cancellationToken));
    }

    [Authorize(Policy = PermissionRequirement.ProjectsManage)]
    [HttpPost]
    public async Task<ActionResult> CreateAsync(string projectKey, UpsertBoardColumnRequest request, CancellationToken cancellationToken)
    {
        var projectId = await service.ResolveProjectIdAsync(projectKey);
        if (projectId is null)
            return ProjectNotFound(projectKey);

        var result = await service.CreateAsync(projectId.Value, request, cancellationToken);
        if (!result.Succeeded)
            return BadRequest(ErrorPayload(result));

        // The collection is the addressable resource here: a single column has no GET of
        // its own, so the created id is returned in the body rather than as a location.
        return Ok(new { id = result.Value });
    }

    [Authorize(Policy = PermissionRequirement.ProjectsManage)]
    [HttpPut("{id:long}")]
    public async Task<ActionResult> UpdateAsync(string projectKey, long id, UpsertBoardColumnRequest request, CancellationToken cancellationToken)
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
