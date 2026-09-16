namespace PMT.Application.Teams.Dtos;

public sealed record AddTeamMembersRequest(
    List<long> UserIds,
    string TeamRole);
