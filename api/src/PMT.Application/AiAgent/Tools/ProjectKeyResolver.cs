using System.Globalization;
using System.Text.Json;
using PMT.Application.Projects;

namespace PMT.Application.AiAgent.Tools;

/// <summary>
/// Turns the public project key the sprint and board tools are addressed by into the surrogate
/// id their repositories actually take, and applies the conversation's project pin on the way
/// through.
/// </summary>
/// <remarks>
/// <para>Sprints and board columns are the two areas whose REST surface is keyed by the project
/// key (<c>api/v1/projects/{projectKey}/sprints</c>), so the tools mirror that: a user says
/// "the PMT project", not "project 4". Every one of those tools needs the same three steps —
/// resolve the key, fall back to the pinned project when the model omitted it, refuse a project
/// outside the conversation's scope — and a tool that skips the last one silently becomes a
/// cross-project write path. Keeping the sequence in one place is the same reasoning that put
/// the entity checks in <see cref="AgentToolScope"/>.</para>
/// <para>Static, taking the repository as an argument, exactly like
/// <see cref="AgentToolScope.ResolveCommentProjectAsync"/>: there is no state to hold, so there
/// is nothing to register.</para>
/// </remarks>
internal static class ProjectKeyResolver
{
    /// <summary>
    /// Resolves the project a tool call is about. Accepts <c>projectKey</c> first, then a raw
    /// <c>projectId</c>, then the conversation's pinned project, and returns a message the model
    /// can act on when none of the three produce a usable project.
    /// </summary>
    public static async Task<(long ProjectId, string? Error)> ResolveAsync(
        IProjectAccessRepository projectAccess,
        AgentToolContext context,
        JsonElement arguments)
    {
        long? projectId = null;

        var key = ToolArguments.GetString(arguments, "projectKey");
        if (key is { Length: > 0 })
        {
            projectId = await projectAccess.GetProjectIdByKeyAsync(key);
            if (projectId is null or <= 0)
                return (0, $"No project with key '{key}' was found. Use search_projects to look up the right key.");
        }

        // A model that already knows the surrogate id (because search_projects just returned it)
        // should not be forced to round-trip through the key.
        projectId ??= ToolArguments.GetLong(arguments, "projectId");

        // Falling back to the pin is what lets "add a sprint called Sprint 4" work inside a
        // project-scoped conversation without the user repeating the key.
        projectId ??= context.ProjectId;

        if (projectId is null or <= 0)
            return (0, "'projectKey' is required. Use search_projects to resolve the project the user means, or ask them which project this is for.");

        if (!AgentToolScope.IsInScope(context, projectId.Value))
            return (0, $"This conversation is scoped to project {context.ProjectId}. Project {projectId} is out of scope.");

        return (projectId.Value, null);
    }

    /// <summary>
    /// Reads an optional date-only argument. Returns false only when the model supplied
    /// something that is not a date; an absent value is a success with a null result.
    /// </summary>
    /// <remarks>
    /// Parsed with the invariant culture so "2026-03-01" means the first of March whatever the
    /// server's locale is, and reported rather than silently dropped so the model can correct
    /// itself instead of creating a sprint with the date quietly missing.
    /// </remarks>
    public static bool TryGetDateOnly(JsonElement arguments, string name, out DateOnly? value, out string? error)
    {
        value = null;
        error = null;

        var raw = ToolArguments.GetString(arguments, name);
        if (raw is null) return true;

        if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            value = parsed;
            return true;
        }

        // A model that sent a full timestamp meant the day it names.
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
        {
            value = DateOnly.FromDateTime(timestamp);
            return true;
        }

        error = $"'{name}' must be a date in YYYY-MM-DD form.";
        return false;
    }
}
