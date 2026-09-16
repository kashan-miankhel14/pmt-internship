using PMT.Application.Issues;
using PMT.Application.Projects;
using PMT.Application.Tasks;
using PMT.Application.Users;
using PMT.Application.UserStories;
using PMT.Domain.Entities;

namespace PMT.Application.AiAgent.Tools;

/// <summary>
/// The single place where agent tools decide whether a record may be read or written.
/// Centralised deliberately: scope checks scattered across thirteen tools drift apart, and a
/// tool that forgets one silently becomes a data-leak path.
/// </summary>
/// <remarks>
/// <para><b>Known gap — there is no per-user project membership in this schema.</b> The
/// database has no ProjectMember table; <see cref="Project"/> carries only OwnerUserId and
/// DepartmentId, and no repository accepts a user id as a visibility filter. Authorisation in
/// PMT is therefore claim-based only (projects.view, stories.manage, ...), enforced by the
/// orchestrator before a tool runs.</para>
/// <para>What this class enforces today is what is actually enforceable: the record exists, is
/// not soft-deleted, and lies inside the project the conversation is pinned to. That last rule
/// is real containment — it stops a project-scoped chat from reading or mutating a different
/// project — but it is <b>not</b> a substitute for row-level authorisation.</para>
/// <para>To close the gap, add a membership lookup to <see cref="IProjectRepository"/> and call
/// it from <see cref="ResolveProjectAsync"/>. Every tool routes through this method, so that is
/// a one-place change.</para>
/// </remarks>
public sealed class AgentToolScope(IProjectRepository projects, IUserRepository users)
{
    /// <summary>
    /// How many rows a search pulls before applying filters the stored procedures cannot express.
    /// Matches the 200-row ceiling the application services already use.
    /// </summary>
    public const int CandidatePoolSize = 200;

    /// <summary>Longest title the Title columns accept (varchar(250)).</summary>
    public const int MaxTitleLength = 250;

    /// <summary>
    /// True when the project is inside the conversation's pinned scope. An unpinned session
    /// sees every project the caller's permissions allow.
    /// </summary>
    public static bool IsInScope(AgentToolContext context, long projectId) =>
        context.ProjectId is not { } pinned || pinned == projectId;

    /// <summary>
    /// Resolves the project that owns a comment, or null when no parent can be reached.
    /// </summary>
    /// <remarks>
    /// dbo.Comment stores its parent as one of four nullable foreign keys rather than an
    /// EntityType/EntityId pair, so the owning project has to be chased through whichever key is
    /// set. Both the delete tool and the confirmation summarizer route through this method so the
    /// sentence the user approves and the scope check that gates the delete can never disagree
    /// about which project a comment belongs to.
    /// </remarks>
    public static async Task<long?> ResolveCommentProjectAsync(
        IUserStoryRepository stories,
        ITaskRepository tasks,
        IIssueRepository issues,
        long? projectId,
        long? storyId,
        long? taskId,
        long? issueId,
        CancellationToken cancellationToken)
    {
        var owningProjectId = projectId;

        if (owningProjectId is null && storyId is { } story)
            owningProjectId = (await stories.GetByIdAsync(story, cancellationToken))?.ProjectId;

        if (owningProjectId is null && taskId is { } task)
            owningProjectId = (await tasks.GetByIdAsync(task, cancellationToken))?.ProjectId;

        if (owningProjectId is null && issueId is { } issue)
            owningProjectId = (await issues.GetByIdAsync(issue, cancellationToken))?.ProjectId;

        return owningProjectId;
    }

    /// <summary>
    /// Resolves a project for a write, or explains why it is unusable. Write tools must call
    /// this before creating or mutating anything hanging off a project.
    /// </summary>
    public async Task<(Project? Project, string? Error)> ResolveProjectAsync(
        AgentToolContext context, long projectId, CancellationToken cancellationToken)
    {
        if (projectId <= 0)
            return (null, "'projectId' must be a positive project id.");

        if (!IsInScope(context, projectId))
            return (null, $"This conversation is scoped to project {context.ProjectId}. Project {projectId} is out of scope.");

        var project = await projects.GetByIdAsync(projectId, cancellationToken);
        if (project is null || project.IsDeleted)
            return (null, $"Project {projectId} was not found.");

        return (project, null);
    }

    /// <summary>Verifies an assignee exists before it is written to a nullable FK column.</summary>
    public async Task<string?> ValidateUserAsync(long? userId, string argumentName, CancellationToken cancellationToken)
    {
        if (userId is null) return null;

        if (userId <= 0)
            return $"'{argumentName}' must be a positive user id.";

        var user = await users.GetByIdAsync(userId.Value, cancellationToken);
        return user is null || user.IsDeleted ? $"User {userId} was not found." : null;
    }

    public static string? ValidateTitle(string? title) => title switch
    {
        null => "'title' is required and cannot be blank.",
        _ when title.Length > MaxTitleLength => $"'title' must be {MaxTitleLength} characters or fewer.",
        _ => null
    };

    public static string? ValidatePriority(int? priority) =>
        priority is null || priority is >= 1 and <= 5
            ? null
            : "'priority' must be between 1 (highest) and 5 (lowest).";
}
