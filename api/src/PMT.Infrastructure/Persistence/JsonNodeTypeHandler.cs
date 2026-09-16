using System.Data;
using System.Text.Json.Nodes;
using Dapper;

namespace PMT.Infrastructure.Persistence;

/// <summary>
/// Materialises the nvarchar(max) JSON columns behind <c>WorkflowTransition.ConditionJson</c>,
/// <c>ValidatorJson</c> and <c>PostFunctionJson</c>. Dapper has no built-in mapping for
/// <see cref="JsonNode"/>, so without this handler every SP_WORKFLOW_TRANSITION read throws
/// "Error parsing column ... (ConditionJson=... - String)" the first time a transition is
/// evaluated or executed.
/// </summary>
/// <remarks>
/// An empty or whitespace-only column is treated as "no configuration" rather than as malformed
/// JSON, which is what <c>WorkflowEngine.ParseConfig</c> already expects from a null node.
/// </remarks>
public sealed class JsonNodeTypeHandler : SqlMapper.TypeHandler<JsonNode?>
{
    public override JsonNode? Parse(object value)
    {
        if (value is null or DBNull) return null;
        if (value is JsonNode node) return node;
        if (value is string text)
            return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);

        throw new DataException($"Cannot convert {value.GetType().Name} to JsonNode.");
    }

    public override void SetValue(IDbDataParameter parameter, JsonNode? value)
    {
        parameter.DbType = DbType.String;
        parameter.Value = value is null ? DBNull.Value : value.ToJsonString();
    }
}
