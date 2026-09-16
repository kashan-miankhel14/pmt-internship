namespace PMT.Application.Projects.Dtos;

/// <summary>Grants every member of a team access to a project under a project role.</summary>
public sealed record AddProjectTeamRequest(
    long TeamId,
    long ProjectRoleId);
