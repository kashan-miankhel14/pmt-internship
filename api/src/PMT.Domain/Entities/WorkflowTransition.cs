using System.Text.Json;
using System.Text.Json.Nodes;
using PMT.Domain.Common;
using PMT.Domain.Enums;

namespace PMT.Domain.Entities;

/// <summary>
/// A directed edge in a workflow's transition graph, optionally guarded by a condition and a
/// validator and carrying post-functions. ConditionJson / ValidatorJson / PostFunctionJson are
/// JSON objects (or arrays of them) whose "name" keys are resolved by handler dictionaries in
/// <c>WorkflowEngine</c>, which keeps the engine data-driven and extensible.
/// </summary>
public sealed class WorkflowTransition : AuditableEntity
{
    public long WorkflowId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Source status, or null for "from any status".</summary>
    public long? FromStatusId { get; set; }

    public long ToStatusId { get; set; }

    public JsonNode? ConditionJson { get; set; }
    public JsonNode? ValidatorJson { get; set; }
    public JsonNode? PostFunctionJson { get; set; }
    public int Order { get; set; }

    // Projected from the status joins for convenience (not persisted).
    public string? FromStatusCode { get; set; }
    public string? FromStatusName { get; set; }
    public string? ToStatusCode { get; set; }
    public string? ToStatusName { get; set; }
    public WorkflowStatusCategory ToStatusCategory { get; set; }
}
