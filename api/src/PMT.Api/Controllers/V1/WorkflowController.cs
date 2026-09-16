using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PMT.Application.Common.Security;
using PMT.Application.Projects;
using PMT.Application.Workflow;
using PMT.Application.Workflow.Engine;
using PMT.Domain.Enums;

namespace PMT.Api.Controllers.V1;

/// <summary>
/// Read and drive the data-driven workflow for a project addressed by its public key.
/// </summary>
[Authorize(Policy = PermissionRequirement.ProjectsView)]
[ApiController, Route("api/v1/projects/{projectKey}/workflow"), EnableRateLimiting("api")]
public sealed class WorkflowController(
    WorkflowService service,
    IProjectAccessRepository projectAccessRepository) : ControllerBase
{
    [HttpGet("statuses")]
    public async Task<ActionResult<IReadOnlyCollection<WorkflowStatusDto>>> GetStatuses(
        string projectKey, CancellationToken cancellationToken)
    {
        var projectId = await ResolveAsync(projectKey);
        if (projectId is null) return ProjectNotFound(projectKey);
        return Ok(await service.GetStatusesAsync(projectId.Value, cancellationToken));
    }

    [HttpGet("transitions")]
    public async Task<ActionResult<IReadOnlyCollection<WorkflowTransitionDto>>> GetTransitions(
        string projectKey, [FromQuery] string? fromStatusCode, CancellationToken cancellationToken)
    {
        var projectId = await ResolveAsync(projectKey);
        if (projectId is null) return ProjectNotFound(projectKey);
        return Ok(await service.GetTransitionsAsync(projectId.Value, fromStatusCode, cancellationToken));
    }

    [HttpGet("history/{entityType}/{entityId:long}")]
    public async Task<ActionResult<IReadOnlyCollection<IssueHistoryDto>>> GetHistory(
        string projectKey, string entityType, long entityId, CancellationToken cancellationToken)
    {
        var projectId = await ResolveAsync(projectKey);
        if (projectId is null) return ProjectNotFound(projectKey);
        return Ok(await service.GetHistoryAsync(entityType, entityId, cancellationToken));
    }

    private async Task<long?> ResolveAsync(string projectKey)
        => await projectAccessRepository.GetProjectIdByKeyAsync(projectKey);

    private ActionResult ProjectNotFound(string projectKey)
        => NotFound(new { errors = new[] { $"Project with key '{projectKey}' was not found." } });
}
