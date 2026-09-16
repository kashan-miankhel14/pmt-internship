namespace PMT.Application.Teams.Dtos;

/// <summary>
/// Create/update payload for a team.
/// </summary>
/// <remarks>
/// <paramref name="Active"/> is nullable because the same record serves both verbs and the update
/// path is a plain PUT: a body that omits it must leave the flag alone rather than silently
/// reactivating a team somebody deactivated on purpose. SP_TEAM already reads it that way —
/// <c>ISNULL(@IsActive, IsActive)</c> on UPDATE and <c>ISNULL(@IsActive, 1)</c> on INSERT — so null
/// means "unchanged" on update and "active" on create.
/// </remarks>
public sealed record UpsertTeamRequest(
    string Key,
    string Name,
    string? Description,
    long LeadUserId,
    bool? Active = null);
