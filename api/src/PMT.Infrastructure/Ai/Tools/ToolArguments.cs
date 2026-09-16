using System.Text.Json;

namespace PMT.Infrastructure.Ai.Tools;

/// <summary>
/// Defensive readers for model-supplied arguments. The LLM frequently emits values with
/// the wrong JSON type (numbers as strings, single values instead of arrays), so every
/// accessor tolerates that rather than throwing and aborting the turn.
/// </summary>
internal static class ToolArguments
{
    public static string? GetString(JsonElement arguments, string name)
    {
        if (arguments.ValueKind != JsonValueKind.Object) return null;
        if (!arguments.TryGetProperty(name, out var value)) return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => Blank(value.GetString()),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    public static long? GetLong(JsonElement arguments, string name)
    {
        if (arguments.ValueKind != JsonValueKind.Object) return null;
        if (!arguments.TryGetProperty(name, out var value)) return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    public static int GetInt(JsonElement arguments, string name, int fallback, int min, int max)
    {
        var value = GetLong(arguments, name);
        if (value is null) return fallback;
        return (int)Math.Clamp(value.Value, min, max);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
