using System.Text.Json;
using System.Text.Json.Nodes;
using PMT.Application.Tasks;

namespace PMT.Application.Workflow.Engine;

/// <summary>Requires every child task of a story to be in a completed (Done/Cancelled) state.</summary>
/// <remarks>
/// Registered as a singleton and keyed by "SubTasksResolved". For Issues and Tasks there are no
/// child tasks, so the condition is vacuously true.
/// </remarks>
public sealed class SubTasksResolvedCondition(ITaskRepository tasks) : IConditionHandler
{
    public string Name => "SubTasksResolved";

    public async Task<bool> EvaluateAsync(ExecutionContext context, JsonObject config, CancellationToken cancellationToken = default)
    {
        if (context.EntityType != WorkflowEntityKind.Story.ToEntityType())
            return true;

        return await tasks.AreChildTasksResolvedAsync(context.EntityId, cancellationToken);
    }
}

/// <summary>Requires a non-empty comment to be supplied for the transition.</summary>
/// <remarks>Keyed by "CommentRequired". The free-text comment travels on the request / tool call.</remarks>
public sealed class CommentRequiredValidator : IValidatorHandler
{
    public string Name => "CommentRequired";

    public Task<string?> ValidateAsync(ExecutionContext context, JsonObject config, CancellationToken cancellationToken = default)
    {
        var message = config.TryGetPropertyValue("message", out var m) && m is JsonValue mv
            ? mv.GetValue<string>()
            : "A comment is required for this transition.";

        var hasComment = context.Values.TryGetValue("comment", out var c) && c is string s && !string.IsNullOrWhiteSpace(s);
        return hasComment ? Task.FromResult<string?>(null) : Task.FromResult<string?>(message);
    }
}

/// <summary>Requires a named value to be present and non-empty (e.g. "resolution").</summary>
/// <remarks>Keyed by "RequiredField".</remarks>
public sealed class RequiredFieldValidator : IValidatorHandler
{
    public string Name => "RequiredField";

    public Task<string?> ValidateAsync(ExecutionContext context, JsonObject config, CancellationToken cancellationToken = default)
    {
        var field = config.TryGetPropertyValue("field", out var f) && f is JsonValue fv ? fv.GetValue<string>() : null;
        var message = config.TryGetPropertyValue("message", out var m) && m is JsonValue mv ? mv.GetValue<string>() : null;

        if (string.IsNullOrWhiteSpace(field))
            return Task.FromResult<string?>("RequiredField validator is missing its 'field' name.");

        var present = context.Values.TryGetValue(field!, out var v) && v is not null && !IsEmpty(v);
        if (present)
            return Task.FromResult<string?>(null);

        return Task.FromResult<string?>(message ?? $"'{field}' is required for this transition.");
    }

    private static bool IsEmpty(object value)
        => value switch
        {
            string s => string.IsNullOrWhiteSpace(s),
            _ => false
        };
}
