namespace PMT.Application.Projects.Dtos;

/// <summary>Grants a single user direct membership of a project under a project role.</summary>
public sealed record AddProjectMemberRequest(
    long UserId,
    long ProjectRoleId);
