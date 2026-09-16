using System.Text.Json;
using PMT.Application.Comments;
using PMT.Application.Common.Security;
using PMT.Application.Issues;
using PMT.Application.Tasks;
using PMT.Application.UserStories;
using PMT.Domain.Entities;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Adds a comment to a project, story, task or issue, authored by the calling user.</summary>
/// <remarks>
/// <para>dbo.Comment has no EntityType/EntityId columns: SP_COMMENT's INSERT branch writes only
/// ProjectId, UserStoryId, TaskId and IssueId, and its FETCH branch reads the comment back
/// through those same foreign keys. Setting <see cref="Comment.EntityType"/> alone would
/// therefore persist a comment attached to nothing and invisible to every reader, so this tool
/// resolves entityType to the matching foreign key column instead.</para>
/// <para>UserId is taken from the tool context so the agent cannot post as another person.</para>
/// </remarks>
public sealed class AddCommentTool(
    ICommentRepository repository,
    IUserStoryRepository stories,
    ITaskRepository tasks,
    IIssueRepository issues,
    AgentToolScope scope) : IAgentTool
{
    private const int MaxBodyLength = 10_000;

    public string Name => "add_comment";

    public string Description =>
        "Add a comment to a Project, UserStory, Task or Issue. The comment is posted as the "
        + "current user. Anna needs the exact wording and what it is attached to before calling "
        + "this: ask with ask_for_fields if either is missing, then read it back and wait for a yes.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "entityType": {
              "type": "string",
              "enum": ["Project", "UserStory", "Task", "Issue"],
              "description": "The kind of record to comment on."
            },
            "entityId": { "type": "integer", "description": "Id of the record to comment on." },
            "body": { "type": "string", "description": "The comment text." }
          },
          "required": ["entityType", "entityId", "body"]
        }
        """;

    public string? RequiredPermission => PermissionRequirement.CommentsManage;

    public bool IsDestructive => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var rawType = ToolArguments.GetString(arguments, "entityType");
        if (rawType is null)
            return AgentToolResult.Fail("'entityType' is required and must be one of: Project, UserStory, Task, Issue.");

        var entityType = Normalize(rawType);
        if (entityType is null)
            return AgentToolResult.Fail($"Unknown entityType '{rawType}'. Expected one of: Project, UserStory, Task, Issue.");

        var entityId = ToolArguments.GetLong(arguments, "entityId");
        if (entityId is null or <= 0)
            return AgentToolResult.Fail("'entityId' is required and must be a positive id.");

        var body = ToolArguments.GetString(arguments, "body");
        if (body is null)
            return AgentToolResult.Fail("'body' is required and cannot be blank.");

        if (body.Length > MaxBodyLength)
            return AgentToolResult.Fail($"'body' must be {MaxBodyLength} characters or fewer.");

        var comment = new Comment
        {
            UserId = context.UserId,
            Body = body,
            EntityType = entityType,
            EntityId = entityId.Value,
            Active = true,
            InsertedBy = context.UserId
        };

        // Resolve the target, confirm it is in scope, and attach the comment to the correct
        // foreign key. The project id is reported back so the caller can see where it landed.
        long projectId;

        switch (entityType)
        {
            case "Project":
            {
                var (project, error) = await scope.ResolveProjectAsync(context, entityId.Value, cancellationToken);
                if (project is null) return AgentToolResult.Fail(error!);

                projectId = project.Id;
                comment.ProjectId = project.Id;
                break;
            }

            case "UserStory":
            {
                var story = await stories.GetByIdAsync(entityId.Value, cancellationToken);
                if (story is null || story.IsDeleted)
                    return AgentToolResult.Fail($"UserStory {entityId} was not found.");

                if (!AgentToolScope.IsInScope(context, story.ProjectId))
                    return AgentToolResult.Fail($"UserStory {entityId} belongs to another project and is out of scope for this conversation.");

                projectId = story.ProjectId;
                comment.UserStoryId = story.Id;
                break;
            }

            case "Task":
            {
                var task = await tasks.GetByIdAsync(entityId.Value, cancellationToken);
                if (task is null || task.IsDeleted)
                    return AgentToolResult.Fail($"Task {entityId} was not found.");

                if (!AgentToolScope.IsInScope(context, task.ProjectId))
                    return AgentToolResult.Fail($"Task {entityId} belongs to another project and is out of scope for this conversation.");

                projectId = task.ProjectId;
                comment.TaskId = task.Id;
                break;
            }

            default:
            {
                var issue = await issues.GetByIdAsync(entityId.Value, cancellationToken);
                if (issue is null || issue.IsDeleted)
                    return AgentToolResult.Fail($"Issue {entityId} was not found.");

                if (!AgentToolScope.IsInScope(context, issue.ProjectId))
                    return AgentToolResult.Fail($"Issue {entityId} belongs to another project and is out of scope for this conversation.");

                projectId = issue.ProjectId;
                comment.IssueId = issue.Id;
                break;
            }
        }

        var id = await repository.CreateAsync(comment, cancellationToken);
        if (id <= 0)
            return AgentToolResult.Fail("The comment could not be created.");

        var created = await repository.GetByIdAsync(id, cancellationToken);

        return AgentToolResult.Ok(new
        {
            created = true,
            id,
            entityType,
            entityId = entityId.Value,
            projectId,
            body = created?.Content ?? comment.Body,
            userId = created?.UserId ?? comment.UserId,
            insertDate = created?.InsertDate
        });
    }

    private static string? Normalize(string value) =>
        value.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant() switch
        {
            "project" => "Project",
            "userstory" or "story" => "UserStory",
            "task" or "taskitem" => "Task",
            "issue" or "bug" or "defect" => "Issue",
            _ => null
        };
}
