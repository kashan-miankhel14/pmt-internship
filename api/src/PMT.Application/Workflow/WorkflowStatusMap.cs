using PMT.Domain.Enums;
// System.Threading.Tasks.TaskStatus is in scope through the implicit usings, so the domain enum
// is aliased the same way the rest of the solution does it.
using TaskStatus = PMT.Domain.Enums.TaskStatus;

namespace PMT.Application.Workflow;

/// <summary>
/// Maps between the legacy per-entity status enums and the data-driven workflow status codes.
/// The engine is code-based; the three work-item tables keep their own enum columns, so this
/// is the single place that translates between the two worlds.
/// </summary>
public static class WorkflowStatusMap
{
    public static string GetStatusCode(WorkflowEntityKind kind, Enum status) => (kind, status) switch
    {
        (WorkflowEntityKind.Story, StoryStatus s) => s switch
        {
            StoryStatus.Backlog => "BACKLOG",
            StoryStatus.Ready => "SELECTED",
            StoryStatus.InProgress => "IN_PROGRESS",
            StoryStatus.Review => "IN_REVIEW",
            StoryStatus.Done => "DONE",
            StoryStatus.Cancelled => "CANCELLED",
            _ => "BACKLOG"
        },
        (WorkflowEntityKind.Task, TaskStatus t) => t switch
        {
            TaskStatus.ToDo => "BACKLOG",
            TaskStatus.InProgress => "IN_PROGRESS",
            TaskStatus.Blocked => "BLOCKED",
            TaskStatus.Review => "IN_REVIEW",
            TaskStatus.Done => "DONE",
            TaskStatus.Cancelled => "CANCELLED",
            _ => "BACKLOG"
        },
        (WorkflowEntityKind.Issue, IssueStatus i) => i switch
        {
            IssueStatus.Open => "BACKLOG",
            IssueStatus.InProgress => "IN_PROGRESS",
            IssueStatus.Resolved => "DONE",
            IssueStatus.Closed => "CLOSED",
            IssueStatus.Rejected => "CANCELLED",
            _ => "BACKLOG"
        },
        _ => "BACKLOG"
    };

    /// <summary>
    /// Resolves a catalog code back to a legacy enum value for this kind, or null when the code
    /// has no equivalent in the entity's enum (e.g. REOPENED for an Issue). A null result means the
    /// transition cannot be expressed on that entity's status column.
    /// </summary>
    public static Enum? ToEntityStatus(WorkflowEntityKind kind, string code) => (kind, code) switch
    {
        (WorkflowEntityKind.Story, "BACKLOG") => StoryStatus.Backlog,
        (WorkflowEntityKind.Story, "SELECTED") => StoryStatus.Ready,
        (WorkflowEntityKind.Story, "IN_PROGRESS") => StoryStatus.InProgress,
        (WorkflowEntityKind.Story, "BLOCKED") => StoryStatus.InProgress,
        (WorkflowEntityKind.Story, "IN_REVIEW") => StoryStatus.Review,
        (WorkflowEntityKind.Story, "READY_FOR_QA") => StoryStatus.Review,
        (WorkflowEntityKind.Story, "IN_QA") => StoryStatus.Review,
        (WorkflowEntityKind.Story, "DONE") => StoryStatus.Done,
        (WorkflowEntityKind.Story, "CLOSED") => StoryStatus.Done,
        (WorkflowEntityKind.Story, "REOPENED") => StoryStatus.InProgress,
        (WorkflowEntityKind.Story, "CANCELLED") => StoryStatus.Cancelled,

        (WorkflowEntityKind.Task, "BACKLOG") => TaskStatus.ToDo,
        (WorkflowEntityKind.Task, "SELECTED") => TaskStatus.ToDo,
        (WorkflowEntityKind.Task, "IN_PROGRESS") => TaskStatus.InProgress,
        (WorkflowEntityKind.Task, "BLOCKED") => TaskStatus.Blocked,
        (WorkflowEntityKind.Task, "IN_REVIEW") => TaskStatus.Review,
        (WorkflowEntityKind.Task, "READY_FOR_QA") => TaskStatus.Review,
        (WorkflowEntityKind.Task, "IN_QA") => TaskStatus.Review,
        (WorkflowEntityKind.Task, "DONE") => TaskStatus.Done,
        (WorkflowEntityKind.Task, "CLOSED") => TaskStatus.Done,
        (WorkflowEntityKind.Task, "REOPENED") => TaskStatus.InProgress,
        (WorkflowEntityKind.Task, "CANCELLED") => TaskStatus.Cancelled,

        (WorkflowEntityKind.Issue, "BACKLOG") => IssueStatus.Open,
        (WorkflowEntityKind.Issue, "SELECTED") => IssueStatus.Open,
        (WorkflowEntityKind.Issue, "IN_PROGRESS") => IssueStatus.InProgress,
        (WorkflowEntityKind.Issue, "BLOCKED") => IssueStatus.InProgress,
        (WorkflowEntityKind.Issue, "IN_REVIEW") => IssueStatus.InProgress,
        (WorkflowEntityKind.Issue, "READY_FOR_QA") => IssueStatus.InProgress,
        (WorkflowEntityKind.Issue, "IN_QA") => IssueStatus.InProgress,
        (WorkflowEntityKind.Issue, "DONE") => IssueStatus.Resolved,
        (WorkflowEntityKind.Issue, "CLOSED") => IssueStatus.Closed,
        (WorkflowEntityKind.Issue, "REOPENED") => IssueStatus.InProgress,
        (WorkflowEntityKind.Issue, "CANCELLED") => IssueStatus.Rejected,

        _ => null
    };
}
