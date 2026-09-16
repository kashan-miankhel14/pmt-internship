using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.GitLinks;
using PMT.Application.Issues;
using PMT.Application.Tasks;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Lists the git repositories, commits, branches and pull requests linked to one record.</summary>
/// <remarks>
/// <para>Mirrors <c>GET api/v1/git-links?entityType&amp;entityId</c>. SP_GITLINK's FETCH branch
/// matches only ProjectId, TaskId and IssueId, so the vocabulary is project|task|issue; a user
/// story has no git link column and is rejected with that explanation.</para>
/// <para>dbo.GitLink predates the reference vocabulary: ReferenceId is stored in the CommitSha
/// column and ReferenceUrl in PullRequestUrl (see <see cref="Domain.Entities.GitLink"/>). A link
/// to a pull request therefore comes back with referenceType "pull_request" and the PR url in
/// referenceUrl, and an older row written by the commit-centric code path may carry a bare sha
/// with no referenceType at all.</para>
/// </remarks>
public sealed class ListGitLinksTool(
    IGitLinkRepository repository,
    ITaskRepository tasks,
    IIssueRepository issues,
    AgentToolScope scope) : IAgentTool
{
    public string Name => "list_git_links";

    public string Description =>
        "List the git links attached to one record: provider, repository url and the reference "
        + "(type, id and url) each link points at, such as a commit, branch or pull request. Pass "
        + "entityType as project, task or issue (user stories cannot carry git links) together with "
        + "entityId, the id of that record.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "entityType": {
              "type": "string",
              "enum": ["project", "task", "issue"],
              "description": "The kind of record the git links belong to. User stories are not supported."
            },
            "entityId": { "type": "integer", "description": "Id of the project, task or issue." }
          },
          "required": ["entityType", "entityId"]
        }
        """;

    public string? RequiredPermission => PermissionRequirement.GitManage;

    public bool IsDestructive => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (arguments.ValueKind is not (JsonValueKind.Object or JsonValueKind.Undefined))
            return AgentToolResult.Fail("Arguments must be a JSON object.");

        var (entityType, entityId, projectId, error) = await ResolveParentAsync(context, arguments, cancellationToken);
        if (error is not null) return AgentToolResult.Fail(error);

        var links = (await repository.GetForEntityAsync(entityType!, entityId, cancellationToken))
            .Where(x => !x.IsDeleted)
            .OrderByDescending(x => x.Id)
            .Select(x => new
            {
                id = x.Id,
                provider = x.Provider.ToString(),
                repositoryUrl = x.RepositoryUrl,
                // ReferenceType is a nullable column and ReferenceId reads through CommitSha with an
                // empty-string fallback; both are reported as absent rather than as blank text.
                referenceType = string.IsNullOrWhiteSpace(x.ReferenceType) ? null : x.ReferenceType,
                referenceId = string.IsNullOrWhiteSpace(x.ReferenceId) ? null : x.ReferenceId,
                referenceUrl = x.ReferenceUrl,
                linkedByUserId = x.InsertedBy,
                linkedAt = x.InsertDate
            })
            .ToArray();

        return AgentToolResult.Ok(new
        {
            count = links.Length,
            entityType,
            entityId,
            projectId,
            gitLinks = links
        });
    }

    /// <summary>
    /// Reads entityType/entityId, confirms the parent exists and is inside the conversation's
    /// project, and reports the owning project alongside the links.
    /// </summary>
    private async Task<(string? EntityType, long EntityId, long ProjectId, string? Error)> ResolveParentAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var rawType = ToolArguments.GetString(arguments, "entityType");
        if (rawType is null)
            return (null, 0, 0, "'entityType' is required and must be one of: project, task, issue.");

        var entityType = Normalize(rawType);
        if (entityType is null)
            return (null, 0, 0,
                $"Unknown entityType '{rawType}'. Git links hang off a project, task or issue only — "
                + "dbo.GitLink has no user story parent.");

        var entityId = ToolArguments.GetLong(arguments, "entityId");
        if (entityId is null or <= 0)
            return (null, 0, 0, "'entityId' is required and must be a positive id.");

        switch (entityType)
        {
            case "Project":
            {
                var (project, projectError) = await scope.ResolveProjectAsync(context, entityId.Value, cancellationToken);
                if (project is null) return (null, 0, 0, projectError!);

                return (entityType, entityId.Value, project.Id, null);
            }

            case "Task":
            {
                var task = await tasks.GetByIdAsync(entityId.Value, cancellationToken);
                if (task is null || task.IsDeleted)
                    return (null, 0, 0, $"Task {entityId} was not found.");

                if (!AgentToolScope.IsInScope(context, task.ProjectId))
                    return (null, 0, 0, $"Task {entityId} belongs to another project and is out of scope for this conversation.");

                return (entityType, entityId.Value, task.ProjectId, null);
            }

            default:
            {
                var issue = await issues.GetByIdAsync(entityId.Value, cancellationToken);
                if (issue is null || issue.IsDeleted)
                    return (null, 0, 0, $"Issue {entityId} was not found.");

                if (!AgentToolScope.IsInScope(context, issue.ProjectId))
                    return (null, 0, 0, $"Issue {entityId} belongs to another project and is out of scope for this conversation.");

                return (entityType, entityId.Value, issue.ProjectId, null);
            }
        }
    }

    /// <summary>
    /// Maps the model's wording onto the three parents SP_GITLINK understands. Story aliases
    /// deliberately fall through to null so they are reported as unsupported.
    /// </summary>
    private static string? Normalize(string value) =>
        value.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant() switch
        {
            "project" => "Project",
            "task" or "taskitem" => "Task",
            "issue" or "bug" or "defect" => "Issue",
            _ => null
        };
}
