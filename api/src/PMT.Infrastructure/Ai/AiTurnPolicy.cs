using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PMT.Application.AiAgent;

namespace PMT.Infrastructure.Ai;

/// <summary>
/// Resolves the turn budget from the provider that is actually going to answer.
/// </summary>
/// <remarks>
/// <para>Every read is defensive. Binding <c>IOptions&lt;T&gt;.Value</c> throws when a section
/// carries a value of the wrong shape (for example <c>"MaxSteps": "three"</c>), and this type is
/// constructed on the path that builds the orchestrator — so an unguarded read would turn a
/// configuration typo into an opaque 500 before the request ever reached the AI client. Falling
/// back to the compiled defaults keeps the request alive long enough for the chat client to
/// report the real problem as a 503 with an actionable message.</para>
/// </remarks>
public sealed class AiTurnPolicy(
    IOptions<AiOptions> aiOptions,
    IOptions<OllamaOptions> ollamaOptions,
    IOptions<GeminiOptions> geminiOptions,
    ILogger<AiTurnPolicy> logger) : IAiTurnPolicy
{
    private AiChatProvider Provider => Read(aiOptions, AiOptions.SectionName).ResolvedChatProvider;

    public string ProviderName => Provider.ToString();

    public int MaxSteps => Provider == AiChatProvider.Gemini
        ? Read(geminiOptions, GeminiOptions.SectionName).EffectiveMaxSteps
        : Read(ollamaOptions, OllamaOptions.SectionName).EffectiveMaxSteps;

    public TimeSpan TotalTurnTimeout
    {
        get
        {
            var providerBudget = Provider == AiChatProvider.Gemini
                ? Read(geminiOptions, GeminiOptions.SectionName).TotalTurnTimeout
                : Read(ollamaOptions, OllamaOptions.SectionName).TotalTurnTimeout;

            var ceiling = Read(aiOptions, AiOptions.SectionName).MaxTurnTimeout;

            return providerBudget < ceiling ? providerBudget : ceiling;
        }
    }

    public bool AllowTextToolCalls => Read(aiOptions, AiOptions.SectionName).AllowTextToolCalls;

    /// <summary>Reads an options section, falling back to its defaults if binding fails.</summary>
    private T Read<T>(IOptions<T> options, string sectionName) where T : class, new()
    {
        try
        {
            return options.Value;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The '{Section}' configuration section could not be bound; falling back to defaults for this turn.",
                sectionName);

            return new T();
        }
    }
}
