using System.Text.Json;
using System.Text.Json.Nodes;
using PMT.Application.Common.Interfaces;
using PMT.Domain.Entities;
using PMT.Domain.Enums;
using PMT.Domain.Exceptions;

namespace PMT.Application.Workflow.Engine;

/// <summary>
/// The data-driven workflow engine. It resolves the project's workflow, evaluates the available
/// transitions for a status code, and — on execution — runs the configured conditions and
/// validators (by JSON "name") and reports the resulting target status plus whether a completion
/// date should be stamped. It never mutates a work item itself; the calling service applies the
/// result and persists history.
/// </summary>
public sealed class WorkflowEngine(
    IWorkflowRepository repository,
    ICurrentUserService currentUser,
    IEnumerable<IConditionHandler> conditionHandlers,
    IEnumerable<IValidatorHandler> validatorHandlers) : IWorkflowEngine
{
    private readonly Dictionary<string, IConditionHandler> _conditions =
        conditionHandlers.ToDictionary(h => h.Name, StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IValidatorHandler> _validators =
        validatorHandlers.ToDictionary(h => h.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>All transitions reachable from <paramref name="fromStatusCode"/> in the project's workflow.</summary>
    public async Task<IReadOnlyCollection<WorkflowTransition>> EvaluateAsync(
        long projectId, string fromStatusCode, CancellationToken cancellationToken = default)
        => await repository.GetAvailableTransitionsAsync(projectId, fromStatusCode, cancellationToken);

    /// <summary>Executes a transition identified by target status code.</summary>
    public async Task<ExecuteResult> ExecuteAsync(
        WorkflowEntityKind kind, long projectId, long entityId, string fromStatusCode, string toStatusCode,
        IDictionary<string, object?>? values = null, CancellationToken cancellationToken = default)
        => await ExecuteCoreAsync(kind, projectId, entityId, fromStatusCode,
            await repository.GetTransitionAsync(projectId, fromStatusCode, toStatusCode, cancellationToken),
            values, cancellationToken);

    /// <summary>Executes a transition identified by its id.</summary>
    public async Task<ExecuteResult> ExecuteAsync(
        WorkflowEntityKind kind, long projectId, long entityId, string fromStatusCode, long transitionId,
        IDictionary<string, object?>? values = null, CancellationToken cancellationToken = default)
        => await ExecuteCoreAsync(kind, projectId, entityId, fromStatusCode,
            await repository.GetTransitionByIdAsync(projectId, transitionId, cancellationToken),
            values, cancellationToken);

    private async Task<ExecuteResult> ExecuteCoreAsync(
        WorkflowEntityKind kind, long projectId, long entityId, string fromStatusCode,
        WorkflowTransition? transition, IDictionary<string, object?>? values, CancellationToken cancellationToken)
    {
        if (transition is null)
            throw new ValidationException(new[]
            {
                $"No workflow transition exists from '{fromStatusCode}' to the requested status for this project. " +
                "Check the available transitions and try one of those."
            });

        var context = new ExecutionContext
        {
            EntityId = entityId,
            // Not kind.ToString(): the discriminator a story is stored and compared under is
            // "UserStory". See WorkflowEntityKindExtensions.
            EntityType = kind.ToEntityType(),
            ProjectId = projectId,
            InitiatorUserId = currentUser.UserId,
            FromStatusCode = fromStatusCode,
            ToStatusCode = transition.ToStatusCode!,
            ToCategory = transition.ToStatusCategory,
            Values = values ?? new Dictionary<string, object?>()
        };

        // Conditions: the transition is only offered/executable when every condition holds.
        foreach (var config in ParseConfig(transition.ConditionJson))
        {
            var name = ConfigName(config);
            if (name is null) continue;
            if (!_conditions.TryGetValue(name, out var handler))
                throw new PMT.Domain.Exceptions.ValidationException(new[] { $"Workflow condition handler '{name}' is not registered." });
            if (!await handler.EvaluateAsync(context, config, cancellationToken))
                throw new ValidationException(new[] { $"Condition '{name}' for transition '{transition.Name}' is not satisfied." });
        }

        // Validators: each returns a user-facing error or null.
        var errors = new List<string>();
        foreach (var config in ParseConfig(transition.ValidatorJson))
        {
            var name = ConfigName(config);
            if (name is null) continue;
            if (!_validators.TryGetValue(name, out var handler))
                throw new PMT.Domain.Exceptions.ValidationException(new[] { $"Workflow validator handler '{name}' is not registered." });
            var error = await handler.ValidateAsync(context, config, cancellationToken);
            if (error is not null) errors.Add(error);
        }

        if (errors.Count > 0)
            throw new ValidationException(errors);

        return new ExecuteResult
        {
            Transition = transition,
            ToStatusCode = transition.ToStatusCode!,
            ToStatusName = transition.ToStatusName!,
            ToCategory = transition.ToStatusCategory,
            ToStatusId = transition.ToStatusId
            // StampDoneDate is derived from ToCategory by ExecuteResult itself; it is not settable.
        };
    }

    /// <summary>Parses a condition/validator JSON node (object or array of objects) into configs.</summary>
    private static IEnumerable<JsonObject> ParseConfig(JsonNode? node)
    {
        if (node is null) yield break;
        if (node is JsonArray array)
        {
            foreach (var item in array)
                if (item is JsonObject o) yield return o;
        }
        else if (node is JsonObject obj)
        {
            yield return obj;
        }
    }

    private static string? ConfigName(JsonObject config)
        => config.TryGetPropertyValue("name", out var n) && n is JsonValue nv ? nv.GetValue<string?>() : null;
}
