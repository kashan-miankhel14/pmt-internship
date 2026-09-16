using System.Text.Json;

namespace PMT.Application.AiAgent.Tools;

/// <summary>
/// Allows Anna to ask the user for specific missing fields when creating or updating entities.
/// Instead of inventing values, Anna calls this tool to request each required field from the
/// user, one at a time.
/// </summary>
/// <remarks>
/// <para>The tool performs no work of its own: it validates the request and hands the field list
/// straight back as the observation, which is the cue for the model to put the questions to the
/// user in ordinary language. Nothing is read and nothing is written, so it needs no permission
/// and can never be destructive.</para>
/// <para>It exists because a model with only write tools available will fill the gaps itself —
/// a plausible title, a guessed project id, an invented assignee. Giving it an explicit way to
/// say "I need more from you" is what turns that guess into a question.</para>
/// </remarks>
public sealed class AskForFieldsTool : IAgentTool
{
    /// <summary>
    /// Longest field list accepted. A model asking for more than this is building a form rather
    /// than holding a conversation, and the reply would be unreadable.
    /// </summary>
    private const int MaxFields = 12;

    public string Name => "ask_for_fields";

    public string Description =>
        "Ask the user for one or more missing values before creating or updating an entity. "
        + "Use this whenever you do not have enough information from the user's request, instead of "
        + "guessing a title, id, status, assignee, priority or any other value. Ask for the required "
        + "fields first and collect them one at a time.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "entityType": {
              "type": "string",
              "description": "Type of entity being created (task, story, issue, project)"
            },
            "missingFields": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "name": { "type": "string" },
                  "label": { "type": "string" },
                  "required": { "type": "boolean" },
                  "type": { "type": "string" }
                }
              },
              "description": "Fields that need user input"
            }
          },
          "required": ["entityType", "missingFields"]
        }
        """;

    public string? RequiredPermission => null;

    public bool IsDestructive => false;

    /// <summary>
    /// Echoes the requested fields back to the model. The observation is a prompt to ask, not
    /// the answer: the values themselves can only come from the user's next message.
    /// </summary>
    public Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var entityType = ToolArguments.GetString(arguments, "entityType");
        if (entityType is null)
            return Fail("'entityType' is required. Name the thing you are trying to create or update, for example 'task' or 'story'.");

        if (arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty("missingFields", out var fields)
            || fields.ValueKind != JsonValueKind.Array
            || fields.GetArrayLength() == 0)
        {
            return Fail("'missingFields' is required and must be a non-empty array of objects shaped { name, label, required }.");
        }

        if (fields.GetArrayLength() > MaxFields)
            return Fail($"Ask for at most {MaxFields} fields at a time. Start with the ones the {entityType} cannot be created without.");

        var requested = new List<object>();
        foreach (var field in fields.EnumerateArray())
        {
            if (field.ValueKind != JsonValueKind.Object) continue;

            var name = ToolArguments.GetString(field, "name");
            if (name is null) continue;

            requested.Add(new
            {
                name,
                label = ToolArguments.GetString(field, "label") ?? name,
                required = IsRequired(field),
                type = ToolArguments.GetString(field, "type") ?? "string"
            });
        }

        if (requested.Count == 0)
            return Fail("Every entry in 'missingFields' needs a 'name'. Name the fields you still need from the user.");

        return Task.FromResult(AgentToolResult.Ok(new
        {
            awaitingUserInput = true,
            entityType,
            fields = requested,
            instruction =
                "Do not create or update anything yet. Ask the user for these values in plain language, "
                + "starting with the first required one and asking for a single value per message. "
                + "Wait for the user's answer before asking for the next field."
        }));
    }

    /// <summary>
    /// A field the model bothered to ask about is treated as required unless it says otherwise,
    /// so a missing or unreadable flag errs towards asking rather than towards guessing.
    /// </summary>
    private static bool IsRequired(JsonElement field) =>
        !string.Equals(ToolArguments.GetString(field, "required"), "false", StringComparison.OrdinalIgnoreCase);

    private static Task<AgentToolResult> Fail(string error) =>
        Task.FromResult(AgentToolResult.Fail(error));
}
