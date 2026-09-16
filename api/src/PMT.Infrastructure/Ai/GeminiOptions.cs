namespace PMT.Infrastructure.Ai;

/// <summary>
/// Configuration for the Google Gemini chat backend, bound from the "Gemini" section.
/// </summary>
/// <remarks>
/// There is no BaseUrl here on purpose: the endpoint host is fixed by
/// <see cref="DependencyInjection"/>, the model is a path segment, and the API key is sent as an
/// <c>x-goog-api-key</c> request header rather than as part of the URI.
/// </remarks>
public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    /// <summary>
    /// Model used when <see cref="ChatModel"/> is missing or blank.
    /// </summary>
    /// <remarks>
    /// gemini-3.5-flash-lite is the lowest-cost flash-lite tier, supports function calling
    /// and responds correctly under the current API key (verified). It is set as the default
    /// so an absent section still resolves to a working, budget-friendly model.
    /// </remarks>
    public const string DefaultChatModel = "gemini-3.5-flash-lite";

    /// <summary>
    /// Gemini API key. Must come from user secrets or the environment
    /// (<c>Gemini__ApiKey</c>) — never from a checked-in configuration file. Sent to Google in
    /// the <c>x-goog-api-key</c> header; it is never placed in a URI and never logged.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Model used for chat completions.</summary>
    public string ChatModel { get; set; } = DefaultChatModel;

    /// <summary>Maximum tool-calling iterations before the agent must answer.</summary>
    public int MaxSteps { get; set; } = 3;

    /// <summary>Per-request HTTP timeout for the Gemini API.</summary>
    public int RequestTimeoutSeconds { get; set; } = 60;

    public TimeSpan RequestTimeout =>
        TimeSpan.FromSeconds(RequestTimeoutSeconds <= 0 ? 60 : Math.Clamp(RequestTimeoutSeconds, 10, 300));

    /// <summary>
    /// Wall-clock budget for a whole turn: three request timeouts.
    /// </summary>
    /// <remarks>
    /// This is the provider's own view of a turn. The effective budget is the smaller of this
    /// and <see cref="AiOptions.MaxTurnTimeout"/>; see <see cref="AiTurnPolicy"/>.
    /// </remarks>
    public TimeSpan TotalTurnTimeout => TimeSpan.FromSeconds(RequestTimeout.TotalSeconds * 3);

    /// <summary>Step budget clamped to a sane range.</summary>
    public int EffectiveMaxSteps => Math.Clamp(MaxSteps <= 0 ? 3 : MaxSteps, 1, 15);

    /// <summary>Configured model with the optional "models/" prefix removed, or the default.</summary>
    public string EffectiveChatModel
    {
        get
        {
            var value = string.IsNullOrWhiteSpace(ChatModel) ? DefaultChatModel : ChatModel.Trim();

            return value.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
                ? value["models/".Length..]
                : value;
        }
    }

    /// <summary>True once an API key has been supplied out of band.</summary>
    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);
}
