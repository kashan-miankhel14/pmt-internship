namespace PMT.Application.Common.Caching;

/// <summary>
/// Process-local cache for read-mostly data that the UI re-reads on every CRUD page
/// (departments, roles, users) and for report queries that are expensive relative to how
/// often their inputs change.
/// </summary>
/// <remarks>
/// <para>
/// Entries belong to a <em>region</em>. <see cref="Invalidate"/> drops every key in a region
/// at once, so a mutation does not need to know which paged or keyed variants happen to be
/// materialised. This is what makes caching a paged list safe: one write evicts all pages.
/// </para>
/// <para>
/// The cache is in-process only. On a multi-instance deployment a write handled by one node
/// does not evict the other nodes' copies, so every entry also carries a short absolute TTL
/// that bounds staleness to the values in <see cref="CacheRegions"/>.
/// </para>
/// <para>
/// Caching happens above the repository layer and below the controller, so it never bypasses
/// an authorization check: the endpoint's policy is evaluated before the service is called.
/// </para>
/// </remarks>
public interface ILookupCache
{
    /// <summary>
    /// Returns the cached value for <paramref name="key"/> within <paramref name="region"/>,
    /// invoking <paramref name="factory"/> on a miss and caching its result for
    /// <paramref name="timeToLive"/>.
    /// </summary>
    /// <remarks>
    /// Null results are not cached, and a factory that throws caches nothing.
    /// </remarks>
    Task<T> GetOrCreateAsync<T>(
        string region,
        string key,
        TimeSpan timeToLive,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken = default);

    /// <summary>Evicts every entry in <paramref name="region"/>.</summary>
    void Invalidate(string region);
}
