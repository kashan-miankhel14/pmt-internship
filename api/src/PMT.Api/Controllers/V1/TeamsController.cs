using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PMT.Application.Common.Models;
using PMT.Application.Common.Security;
using PMT.Application.Teams;
using PMT.Application.Teams.Dtos;
using PMT.Domain.Common;

namespace PMT.Api.Controllers.V1;

[Authorize(Policy = PermissionRequirement.ProjectsView)]
[ApiController, Route("api/v1/teams"), EnableRateLimiting("api")]
public sealed class TeamsController(TeamService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<TeamDto>>> ListAsync([FromQuery] int page = 1, [FromQuery] int pageSize = 25, [FromQuery] string? search = null)
        => Ok(await service.ListAsync(page, pageSize, search));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<TeamDto>> GetAsync(long id)
    {
        var result = await service.GetByIdAsync(id);
        return result.Succeeded ? Ok(result.Value) : NotFound(ErrorPayload(result));
    }

    [Authorize(Policy = PermissionRequirement.ProjectsManage)]
    [HttpPost]
    public async Task<ActionResult> CreateAsync(UpsertTeamRequest request)
    {
        var result = await service.CreateAsync(request);
        if (!result.Succeeded)
            return BadRequest(ErrorPayload(result));

        var id = result.Value;
        // MVC trims the "Async" suffix when it derives action names, so the target of this
        // link is the action "Get", not "GetAsync".
        return CreatedAtAction("Get", new { id }, new { id });
    }

    [Authorize(Policy = PermissionRequirement.ProjectsManage)]
    [HttpPut("{id:long}")]
    public async Task<ActionResult> UpdateAsync(long id, UpsertTeamRequest request)
        => Resolve(await service.UpdateAsync(id, request));

    [Authorize(Policy = PermissionRequirement.ProjectsManage)]
    [HttpDelete("{id:long}")]
    public async Task<ActionResult> DeleteAsync(long id)
        => Resolve(await service.DeleteAsync(id));

    [HttpGet("{id:long}/members")]
    public async Task<ActionResult<PagedResult<TeamMemberDto>>> ListMembersAsync(long id, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
        => Ok(await service.ListMembersAsync(id, page, pageSize));

    /// <summary>
    /// Adds several users to a team in one call. The service exposes a single-member
    /// operation, so failures are collected per user rather than aborting the batch: a
    /// user who is already a member does not prevent the others from being added.
    /// </summary>
    [Authorize(Policy = PermissionRequirement.ProjectsManage)]
    [HttpPost("{id:long}/members")]
    public async Task<ActionResult> AddMembersAsync(long id, AddTeamMembersRequest request)
    {
        if (request.UserIds is null || request.UserIds.Count == 0)
            return BadRequest(new { errors = new[] { "At least one user id is required." } });

        var errors = new List<string>();
        foreach (var userId in request.UserIds.Distinct())
        {
            var result = await service.AddMemberAsync(id, userId, request.TeamRole);
            if (!result.Succeeded)
                errors.AddRange(result.Errors.Select(error => $"User {userId}: {error}"));
        }

        return errors.Count == 0 ? NoContent() : BadRequest(new { errors });
    }

    [Authorize(Policy = PermissionRequirement.ProjectsManage)]
    [HttpDelete("{teamId:long}/members/{userId:long}")]
    public async Task<ActionResult> RemoveMemberAsync(long teamId, long userId)
        => Resolve(await service.RemoveMemberAsync(teamId, userId));

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
