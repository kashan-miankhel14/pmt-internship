using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.GitLinks;
using PMT.Application.GitLinks.Dtos;
using PMT.Application.Issues;
using PMT.Application.Tasks;
using PMT.Domain.Enums;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Links a repository, commit, branch or pull request to a project, task or issue.</summary>
/// <remarks>
/// <para>Goes through <see cref="GitLinkService.CreateAsync"/>, the same path
/// <c>POST api/v1/git-links</c> uses, so the parent foreign key is resolved and the row is stamped
/// exactly as it would be from the API; the service takes the author from the caller's identity, so
/// the agent cannot record the link as somebody else.</para>
/// <para><b>referenceType and referenceId are non-nullable on <see cref="CreateGitLinkRequest"/></b>
/// and the service calls Trim() on both, so an omitted value is sent as an empty string rather than
/// null — passing null there would be a NullReferenceException inside the service.</para>
/// <para>The reference is stored in the legacy columns: referenceId lands in CommitSha and
/// referenceUrl in PullRequestUrl, which is why the lengths accepted here are those columns'
/// (100 and 500 characters), not the vocabulary's.</para>
/// </remarks>
public sealed class CreateGitLinkTool(
    GitLinkService service,
    ITaskRepository tasks,
    IIssueRepository issues,
    AgentToolScope scope) : IAgentTool
{
    /// <summary>varchar(500) on GitLink.RepositoryUrl and GitLink.PullRequestUrl.</summary>
    private const int MaxUrlLength = 500;

    /// <summary>varchar(50) on GitLink.ReferenceType.</summary>
    private const int MaxReferenceTypeLength = 50;

    /// <summary>varchar(100) on GitLink.CommitSha, which backs ReferenceId.</summary>
    private const int MaxReferenceIdLength = 100;

    public string Name => "create_git_link";

    public string Description =>
        "Link a git repository, commit, branch or pull request to a record — for example attaching a "
        + "PR to the task it implements. Pass entityType as project, task or issue (user stories "
        + "cannot carry git links), entityId, the provider (GitHub, GitLab, AzureDevOps or Other) and "
        + "the repositoryUrl; referenceType (commit, branch or pull_request), referenceId (the sha, "
        + "branch name or PR number) and referenceUrl are optional. Ask for the url with "
        + "ask_for_fields rather than guessing it.";

    public string ParametersJsonSchema => $$"""
        {
          "type": "object",
          "properties": {
            "entityType": {
              "type": "string",
              "enum": ["project", "task", "issue"],
              "description": "The kind of record to link the git reference to. User stories are not supported."
            },
            "entityId": { "type": "integer", "description": "Id of the project, task or issue." },
            "provider": {
              "type": "string",
              "enum": ["GitHub", "GitLab", "AzureDevOps", "Other"],
              "description": "Which git host the repository lives on."
            },
            "repositoryUrl": { "type": "string", "description": "Url of the repository, up to {{MaxUrlLength}} characters." },
            "referenceType": { "type": "string", "description": "What is being linked, for example commit, branch or pull_request. Up to {{MaxReferenceTypeLength}} characters." },
            "referenceId": { "type": "string", "description": "Identifier of the reference: a commit sha, a branch name or a pull request number. Up to {{MaxReferenceIdLength}} characters." },
            "referenceUrl": { "type": "string", "description": "Direct url to the commit, branch or pull request, up to {{MaxUrlLength}} characters." }
          },
          "required": ["entityType", "entityId", "provider", "repositoryUrl"]
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

        if (!ToolArguments.TryGetEnum<GitProvider>(arguments, "provider", out var provider, out var providerError))
            return AgentToolResult.Fail(providerError!);

        if (provider is null)
            return AgentToolResult.Fail("'provider' is required and must be one of: GitHub, GitLab, AzureDevOps, Other.");

        var repositoryUrl = ToolArguments.GetString(arguments, "repositoryUrl");
        if (repositoryUrl is null)
            return AgentToolResult.Fail("'repositoryUrl' is required and cannot be blank.");

        if (repositoryUrl.Length > MaxUrlLength)
            return AgentToolResult.Fail($"'repositoryUrl' must be {MaxUrlLength} characters or fewer.");

        // Optional, but the request record and the service both treat them as non-nullable, so an
        // omitted value becomes an empty string rather than null.
        var referenceType = ToolArguments.GetString(arguments, "referenceType") ?? string.Empty;
        if (referenceType.Length > MaxReferenceTypeLength)
            return AgentToolResult.Fail($"'referenceType' must be {MaxReferenceTypeLength} characters or fewer.");

        var referenceId = ToolArguments.GetString(arguments, "referenceId") ?? string.Empty;
        if (referenceId.Length > MaxReferenceIdLength)
            return AgentToolResult.Fail($"'referenceId' must be {MaxReferenceIdLength} characters or fewer.");

        var referenceUrl = ToolArguments.GetString(arguments, "referenceUrl");
        if (referenceUrl is { Length: > MaxUrlLength })
            return AgentToolResult.Fail($"'referenceUrl' must be {MaxUrlLength} characters or fewer.");

        var request = new CreateGitLinkRequest(
            entityType!, entityId, provider.Value, repositoryUrl, referenceType, referenceId, referenceUrl);

        var id = await service.CreateAsync(request, cancellationToken);
        if (id <= 0)
            return AgentToolResult.Fail("The git link could not be created.");

        return AgentToolResult.Ok(new
        {
            created = true,
            id,
            message = $"Linked {(referenceType.Length == 0 ? "repository" : referenceType)} "
                + $"{(referenceId.Length == 0 ? repositoryUrl : referenceId)} to {entityType} #{entityId}.",
            entityType,
            entityId,
            projectId,
            provider = provider.Value.ToString(),
            repositoryUrl,
            referenceType = referenceType.Length == 0 ? null : referenceType,
            referenceId = referenceId.Length == 0 ? null : referenceId,
            referenceUrl
        });
    }

    /// <summary>
    /// Reads entityType/entityId, confirms the parent exists and is inside the conversation's
    /// project, and reports the owning project so the caller can see where the link landed.
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
    /// Maps the model's wording onto the three parents the service resolves to a foreign key.
    /// Story aliases deliberately fall through to null so they are reported as unsupported.
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
