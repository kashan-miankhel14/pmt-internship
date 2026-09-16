namespace PMT.Infrastructure.Ai;

/// <summary>
/// Settings for the scheduled reindex loop, bound from the "Ai:AutoReindex" subsection.
/// </summary>
/// <remarks>
/// <para>Off by default. Every tick embeds whatever has changed since the last one, which costs
/// real calls against the embedding backend, so a deployment has to ask for it rather than
/// inherit it — a developer machine with no Ollama running would otherwise log a failure every
/// quarter of an hour.</para>
/// <para>The loop is a subsection of "Ai" rather than a section of its own because it is part of
/// the same AI stack as <see cref="AiOptions"/>; the binder ignores it when binding the parent
/// section, so the two coexist without either having to know about the other.</para>
/// </remarks>
public sealed class AiAutoReindexOptions
{
    public const string SectionName = "Ai:AutoReindex";

    /// <summary>
    /// Whether the background loop runs at all. When false the hosted service exits at startup
    /// and costs nothing beyond its registration.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>Minutes between runs. The first run happens one interval after startup.</summary>
    public int IntervalMinutes { get; set; } = 15;

    /// <summary>
    /// When true (the default) each run skips sources whose chunk is already newer than the
    /// source, so a steady-state tick embeds only what actually changed. Set false to re-embed
    /// everything on every tick — appropriate only after an embedding model change, and better
    /// done once through the admin endpoint than on a schedule.
    /// </summary>
    public bool Incremental { get; set; } = true;

    /// <summary>
    /// <see cref="IntervalMinutes"/> clamped to 1 minute .. 24 hours, so a stray 0 or a negative
    /// value cannot turn the loop into a spin.
    /// </summary>
    public TimeSpan Interval =>
        TimeSpan.FromMinutes(IntervalMinutes <= 0 ? 15 : Math.Clamp(IntervalMinutes, 1, 1440));

    /// <summary>
    /// The <c>force</c> flag handed to <see cref="PMT.Application.AiAgent.IAiIndexingService.ReindexAllAsync"/>:
    /// an incremental run is exactly a non-forced one.
    /// </summary>
    public bool Force => !Incremental;
}
