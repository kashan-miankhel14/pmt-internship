namespace PMT.Domain.Enums;

/// <summary>
/// Lifecycle of a sprint. Persisted as the uppercase name ('PLANNED', 'ACTIVE',
/// 'COMPLETED') to satisfy CK_sprint_status; the repository uppercases on write and
/// Dapper parses back case-insensitively.
/// </summary>
public enum SprintStatus
{
    Planned,
    Active,
    Completed
}
