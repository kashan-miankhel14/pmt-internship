using PMT.Domain.Enums;

namespace PMT.Application.Sprints.Dtos;

/// <summary>
/// Create/update payload for a sprint. The owning project is taken from the route key,
/// not the body, so it is deliberately absent here.
/// </summary>
/// <remarks>
/// <paramref name="Status"/> is nullable because the update path is a plain PUT: a client
/// that edits the name or the dates and omits the status must not silently reset a running
/// sprint to PLANNED. Null means "leave the status alone" on update and PLANNED on create;
/// the two guarded transitions (start / complete) remain the way a sprint changes state.
/// <para>
/// <paramref name="Active"/> is nullable for the same reason. An update that changes only the
/// name must not flip a deactivated sprint back to active, so null means "leave Active alone"
/// on update. On create a null falls back to true (see <see cref="SprintService.CreateAsync"/>),
/// matching SP_SPRINT's ISNULL(@Active,1) default and the previous DTO default, so a new sprint
/// is active out of the box.
/// </para>
/// </remarks>
public sealed record UpsertSprintRequest(
    string Name,
    string? Goal,
    DateOnly? StartDate,
    DateOnly? EndDate,
    SprintStatus? Status = null,
    bool? Active = null);
