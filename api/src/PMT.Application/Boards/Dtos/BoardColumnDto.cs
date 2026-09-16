namespace PMT.Application.Boards.Dtos;

public sealed record BoardColumnDto(
    long Id,
    long ProjectId,
    string Name,
    int Ordinal,
    bool CompleteColumn,
    bool Active);
