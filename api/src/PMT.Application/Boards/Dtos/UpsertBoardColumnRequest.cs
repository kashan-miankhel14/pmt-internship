namespace PMT.Application.Boards.Dtos;

/// <summary>
/// Create/update payload for a board column. The owning project is taken from the
/// route key, not the body.
/// </summary>
public sealed record UpsertBoardColumnRequest(
    string Name,
    int Ordinal,
    bool CompleteColumn = false);
