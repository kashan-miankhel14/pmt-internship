using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PMT.Application.Common.Security;
using PMT.Application.Issues;
using PMT.Application.Issues.Dtos;
using PMT.Application.Common.Models;
using PMT.Application.Workflow;
using PMT.Application.Workflow.Engine;
using PMT.Domain.Exceptions;
namespace PMT.Api.Controllers.V1;
[Authorize(Policy=PermissionRequirement.IssuesView)]
[ApiController, Route("api/v1/issues"), EnableRateLimiting("api")]
public sealed class IssuesController(IssueService service, WorkflowService workflowService) : ControllerBase
{
    [HttpGet]
    public Task<PMT.Application.Common.Models.PagedResult<IssueDto>> Get([FromQuery] int page=1, [FromQuery] int pageSize=25, [FromQuery] string? search=null, CancellationToken cancellationToken=default)
        => service.GetPagedAsync(page,pageSize,search,cancellationToken);
    [HttpGet("{id:long}")]
    public Task<IssueDto> GetById(long id, CancellationToken cancellationToken) => service.GetByIdAsync(id,cancellationToken);
    [Authorize(Policy=PermissionRequirement.IssuesManage)]
    [HttpPost]
    public async Task<IActionResult> Create(UpsertIssueRequest request, CancellationToken cancellationToken)
    { var id=await service.CreateAsync(request,cancellationToken); return CreatedAtAction(nameof(GetById),new { id },new { id }); }
    [Authorize(Policy=PermissionRequirement.IssuesManage)]
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, UpsertIssueRequest request, CancellationToken cancellationToken)
    { await service.UpdateAsync(id,request,cancellationToken); return NoContent(); }
    [Authorize(Policy=PermissionRequirement.IssuesManage)]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    { await service.DeleteAsync(id,cancellationToken); return NoContent(); }

    /// <summary>
    /// Runs the project's workflow engine to move an issue to a new status. Either
    /// <c>toStatusCode</c> (a workflow status code such as "DONE") or <c>transitionId</c> may be
    /// supplied. When the transition requires it (for example Blocked), a <c>comment</c> is
    /// required. Returns the resulting status and the transitions available from it.
    /// </summary>
    [Authorize(Policy=PermissionRequirement.IssuesManage)]
    [HttpPost("{id:long}/transitions")]
    public async Task<ActionResult<WorkflowResultDto>> Transition(long id, [FromBody] IssueTransitionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await workflowService.TransitionIssueAsync(id, WorkflowEntityKind.Issue, request.ToStatusCode, request.TransitionId, request.Comment, cancellationToken);
            return Ok(result);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { errors = ex.Errors });
        }
    }
}

/// <summary>Body for POST /api/v1/issues/{id}/transitions.</summary>
public sealed record IssueTransitionRequest(string? ToStatusCode = null, long? TransitionId = null, string? Comment = null);
