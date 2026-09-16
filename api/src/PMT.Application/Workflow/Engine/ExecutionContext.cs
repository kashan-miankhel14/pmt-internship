using System.Text.Json.Nodes;
using PMT.Domain.Enums;

namespace PMT.Application.Workflow.Engine;

/// <summary>
/// Per-transition context handed to every condition and validator handler. Handlers read the
/// fields they care about and may write computed values back into <see cref="Values"/> (for
/// example a "resolution" derived from a completion) so a later handler in the same chain can
/// see it.
/// </summary>
public sealed class ExecutionContext
{
    public long EntityId { get; init; }
    public string EntityType { get; init; } = "Issue";
    public long ProjectId { get; init; }
    public long? InitiatorUserId { get; init; }

    public string FromStatusCode { get; init; } = string.Empty;
    public string ToStatusCode { get; init; } = string.Empty;
    public WorkflowStatusCategory ToCategory { get; init; }

    /// <summary>Caller-supplied values keyed by name (assigneeUserId, comment, resolution, ...).</summary>
    public IDictionary<string, object?> Values { get; init; } = new Dictionary<string, object?>();
}

/// <summary>A guard that must hold for the transition to be offered / executed.</summary>
public interface IConditionHandler
{
    string Name { get; }
    Task<bool> EvaluateAsync(ExecutionContext context, JsonObject config, CancellationToken cancellationToken = default);
}

/// <summary>A gate that must pass for the transition to execute; returns a user-facing error or null.</summary>
public interface IValidatorHandler
{
    string Name { get; }
    Task<string?> ValidateAsync(ExecutionContext context, JsonObject config, CancellationToken cancellationToken = default);
}
