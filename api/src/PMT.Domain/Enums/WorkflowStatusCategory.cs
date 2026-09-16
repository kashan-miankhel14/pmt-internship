namespace PMT.Domain.Enums;

/// <summary>
/// The Jira-like grouping of a workflow status. Drives automatic post-functions (stamping
/// completion dates when an item enters a Done status).
/// </summary>
public enum WorkflowStatusCategory
{
    ToDo,
    InProgress,
    Done
}
