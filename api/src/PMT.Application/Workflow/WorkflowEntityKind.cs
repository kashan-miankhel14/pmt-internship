namespace PMT.Application.Workflow;

/// <summary>The three entity kinds the workflow engine governs.</summary>
public enum WorkflowEntityKind
{
    Story,
    Task,
    Issue
}

/// <summary>
/// Translates a <see cref="WorkflowEntityKind"/> into the entity-type discriminator that is
/// persisted and compared against.
/// </summary>
/// <remarks>
/// The discriminator is deliberately not <c>kind.ToString()</c>: a story is stored as "UserStory"
/// because that is the only story spelling dbo.IssueHistory's CK_issuehistory_entitytype check
/// constraint accepts, the value SP_WORKFLOW_STATUS's STAMP branch matches on, and the value the
/// rest of the system (comments, AI index) already uses. Routing the engine, the history writes
/// and the condition handlers through this one method is what keeps them on a single spelling.
/// </remarks>
public static class WorkflowEntityKindExtensions
{
    /// <summary>The persisted entity-type discriminator for this kind.</summary>
    public static string ToEntityType(this WorkflowEntityKind kind) => kind switch
    {
        WorkflowEntityKind.Story => "UserStory",
        WorkflowEntityKind.Task => "Task",
        WorkflowEntityKind.Issue => "Issue",
        _ => kind.ToString()
    };
}
