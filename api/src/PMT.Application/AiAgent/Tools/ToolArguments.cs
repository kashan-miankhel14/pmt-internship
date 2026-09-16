using System.Globalization;
using System.Text.Json;

namespace PMT.Application.AiAgent.Tools;

/// <summary>
/// Defensive readers for model-supplied arguments. An LLM routinely emits values with the
/// wrong JSON type (numbers as strings, "null" as a string, enums in the wrong casing), so
/// every accessor tolerates that instead of throwing and aborting the turn.
/// </summary>
/// <remarks>
/// <see cref="Has"/> is what makes partial updates possible: it distinguishes "the model did
/// not mention this field" from "the model explicitly sent null to clear it". Update tools
/// must use it rather than treating a null read as "leave unchanged".
/// </remarks>
internal static class ToolArguments
{
    /// <summary>True when the property is present and is not JSON null.</summary>
    public static bool Has(JsonElement arguments, string name) =>
        TryGetValue(arguments, name, out _);

    /// <summary>True when the property is present at all, including an explicit JSON null.</summary>
    public static bool IsMentioned(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object
        && arguments.TryGetProperty(name, out var value)
        && value.ValueKind != JsonValueKind.Undefined;

    public static string? GetString(JsonElement arguments, string name)
    {
        if (!TryGetValue(arguments, name, out var value)) return null;

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
        if (!TryGetValue(arguments, name, out var value)) return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(
                value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    public static int? GetIntOrNull(JsonElement arguments, string name)
    {
        var value = GetLong(arguments, name);
        return value is >= int.MinValue and <= int.MaxValue ? (int?)value.Value : null;
    }

    /// <summary>Reads a bounded integer, clamping rather than failing. Used for paging only.</summary>
    public static int GetInt(JsonElement arguments, string name, int fallback, int min, int max)
    {
        var value = GetLong(arguments, name);
        return value is null ? fallback : (int)Math.Clamp(value.Value, min, max);
    }

    public static decimal? GetDecimal(JsonElement arguments, string name)
    {
        if (!TryGetValue(arguments, name, out var value)) return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            JsonValueKind.String when decimal.TryParse(
                value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    public static DateTime? GetDateTime(JsonElement arguments, string name)
    {
        var raw = GetString(arguments, name);
        if (raw is null) return null;

        return DateTime.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// Reads an optional enum. Returns false only when the model supplied a value that is not a
    /// member of <typeparamref name="TEnum"/>; an absent value is a success with a null result.
    /// The error names the legal values so the model can correct itself on the next step.
    /// </summary>
    public static bool TryGetEnum<TEnum>(JsonElement arguments, string name, out TEnum? value, out string? error)
        where TEnum : struct, Enum
    {
        value = null;
        error = null;

        var raw = GetString(arguments, name);
        if (raw is null)
        {
            // Present but unreadable (e.g. an object or array) is a genuine mistake worth reporting.
            if (Has(arguments, name))
            {
                error = Expected<TEnum>(name);
                return false;
            }

            return true;
        }

        // Models like to write "In Progress" or "in_progress" for InProgress.
        var normalized = raw.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);

        if (Enum.TryParse<TEnum>(normalized, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            value = parsed;
            return true;
        }

        error = Expected<TEnum>(name);
        return false;
    }

    private static string Expected<TEnum>(string name) where TEnum : struct, Enum =>
        $"'{name}' must be one of: {string.Join(", ", Enum.GetNames<TEnum>())}.";

    private static bool TryGetValue(JsonElement arguments, string name, out JsonElement value)
    {
        value = default;
        if (arguments.ValueKind != JsonValueKind.Object) return false;

        return arguments.TryGetProperty(name, out value)
            && value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
