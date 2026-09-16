using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.UserStories;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Soft-deletes a user story the caller can reach.</summary>
/// <remarks>
/// <para>This tool is <see cref="IsDestructive"/>: the orchestrator never runs it straight off the
/// model's request. The turn is paused, the user is shown what is about to be removed, and the
/// call only reaches <see cref="ExecuteAsync"/> after an explicit approval.</para>
/// <para>The delete is the same soft delete the UI performs (<c>IsDeleted</c> plus the deleting
/// user), so nothing is physically destroyed and the row can still be recovered in SQL.</para>
/// </remarks>
public sealed class DeleteStoryTool(IUserStoryRepository repository) : IAgentTool
{
    public string Name => "delete_story";

    public string Description =>
        "Delete a user story. Resolve the exact storyId with search_stories or get_entity first. "
        + "The user is asked to confirm before the deletion actually happens.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "storyId": { "type": "integer", "description": "Id of the story to delete." }
          },
          "required": ["storyId"]
        }
        """;

    public string? RequiredPermission => PermissionRequirement.StoriesManage;

    public bool IsDestructive => true;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var storyId = ToolArguments.GetLong(arguments, "storyId");
        if (storyId is null or <= 0)
            return AgentToolResult.Fail("'storyId' is required and must be a positive story id.");

        var story = await repository.GetByIdAsync(storyId.Value, cancellationToken);
        if (story is null || story.IsDeleted)
            return AgentToolResult.Fail($"UserStory {storyId} was not found.");

        if (!AgentToolScope.IsInScope(context, story.ProjectId))
            return AgentToolResult.Fail($"UserStory {storyId} belongs to another project and is out of scope for this conversation.");

        if (!await repository.DeleteAsync(story.Id, context.UserId, cancellationToken))
            return AgentToolResult.Fail($"UserStory {storyId} could not be deleted.");

        return AgentToolResult.Ok(new
        {
            deleted = true,
            id = story.Id,
            title = story.Title,
            projectId = story.ProjectId
        });
    }
}
