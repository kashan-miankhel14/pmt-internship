using System.Text.Json;
using PMT.Application.Attachments;
using PMT.Application.Common.Security;
using PMT.Application.Issues;
using PMT.Application.Tasks;
using PMT.Application.Users;
using PMT.Domain.Exceptions;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Lists the files attached to one project, task or issue.</summary>
/// <remarks>
/// <para>Mirrors <c>GET api/v1/attachments?entityType&amp;entityId</c>. SP_ATTACHMENT's FETCH branch
/// matches only ProjectId, TaskId and IssueId, so a user story is not a parent an attachment can
/// have: the vocabulary here is deliberately project|task|issue, and a story is rejected with that
/// explanation rather than answered with an empty list the model would read as "no files".</para>
/// <para>dbo.Attachment stores the size as whole kilobytes (FileSizeKb) and
/// <see cref="Domain.Entities.Attachment.FileSize"/> multiplies that back out, so the byte count
/// reported here is accurate only to the kilobyte. Both values are returned so the model can say
/// "about 3 MB" without implying a precision the column does not hold.</para>
/// <para>Uploader names are resolved one lookup per distinct uploader, through
/// <see cref="UserService"/> rather than <see cref="IUserRepository"/> so the read is served from
/// the <c>CacheRegions.Users</c> lookup cache. The procedure returns no join, and the same few
/// people upload most of a record's files, so this stays a handful of reads and usually none at
/// all after the first turn.</para>
/// </remarks>
public sealed class ListAttachmentsTool(
    IAttachmentRepository repository,
    ITaskRepository tasks,
    IIssueRepository issues,
    UserService users,
    AgentToolScope scope) : IAgentTool
{
    public string Name => "list_attachments";

    public string Description =>
        "List the files attached to one record: file name, content type, size, who uploaded it, when, "
        + "and the path to download it. Pass entityType as project, task or issue (files cannot hang "
        + "off a user story) together with entityId, the id of that record.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "entityType": {
              "type": "string",
              "enum": ["project", "task", "issue"],
              "description": "The kind of record the files are attached to. User stories are not supported."
            },
            "entityId": { "type": "integer", "description": "Id of the project, task or issue." }
          },
          "required": ["entityType", "entityId"]
        }
        """;

    public string? RequiredPermission => PermissionRequirement.AttachmentsManage;

    public bool IsDestructive => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (arguments.ValueKind is not (JsonValueKind.Object or JsonValueKind.Undefined))
            return AgentToolResult.Fail("Arguments must be a JSON object.");

        var (entityType, entityId, projectId, error) = await ResolveParentAsync(context, arguments, cancellationToken);
        if (error is not null) return AgentToolResult.Fail(error);

        var attachments = (await repository.GetForEntityAsync(entityType!, entityId, cancellationToken))
            .Where(x => !x.IsDeleted)
            .OrderByDescending(x => x.InsertDate)
            .ThenByDescending(x => x.Id)
            .ToArray();

        // One cache-backed lookup per distinct uploader: an id alone tells the user nothing.
        var uploaderNames = new Dictionary<long, string?>();
        foreach (var uploaderId in attachments.Select(x => x.UploadedByUserId).Distinct())
            uploaderNames[uploaderId] = await ResolveDisplayNameAsync(uploaderId, cancellationToken);

        var results = attachments
            .Select(x => new
            {
                id = x.Id,
                fileName = x.FileName,
                contentType = x.ContentType,
                fileSizeKb = x.FileSizeKb,
                approximateFileSizeBytes = x.FileSize,
                uploadedByUserId = x.UploadedByUserId,
                uploadedBy = uploaderNames.GetValueOrDefault(x.UploadedByUserId),
                uploadedAt = x.InsertDate,
                storedPath = x.FilePath,
                downloadPath = $"/api/v1/attachments/{x.Id}/download"
            })
            .ToArray();

        return AgentToolResult.Ok(new
        {
            count = results.Length,
            entityType,
            entityId,
            projectId,
            attachments = results
        });
    }

    /// <summary>
    /// Reads entityType/entityId, confirms the parent exists and is inside the conversation's
    /// project, and reports the owning project so the caller can see where the files live.
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
                $"Unknown entityType '{rawType}'. Attachments hang off a project, task or issue only — "
                + "dbo.Attachment has no user story parent.");

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
    /// Resolves one uploader's display name through the cached user lookup. A file outlives the
    /// account that uploaded it, and <see cref="UserService.GetByIdAsync"/> reports a missing user
    /// by throwing, so the miss becomes a null name rather than a failed listing.
    /// </summary>
    private async Task<string?> ResolveDisplayNameAsync(long userId, CancellationToken cancellationToken)
    {
        try
        {
            return (await users.GetByIdAsync(userId, cancellationToken)).DisplayName;
        }
        catch (NotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Maps the model's wording onto the three parents SP_ATTACHMENT understands. Story aliases
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
