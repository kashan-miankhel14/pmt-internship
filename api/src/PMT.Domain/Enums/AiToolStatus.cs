namespace PMT.Domain.Enums;

/// <summary>
/// Lifecycle of a single agent tool invocation. Stored as a string in
/// dbo.AiAgentToolCall.Status (varchar(20)); the database defaults to
/// <see cref="Running"/> when a call is created.
/// </summary>
public enum AiToolStatus
{
    Pending,
    Running,
    Completed,
    Failed
}
