using PMT.Domain.Enums;

namespace PMT.Application.Sprints.Dtos;

public sealed record SprintDto(
    long Id,
    long ProjectId,
    string Name,
    string? Goal,
    DateOnly? StartDate,
    DateOnly? EndDate,
    SprintStatus Status,
    bool Active);
