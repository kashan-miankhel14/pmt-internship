using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using PMT.Application.Common.Caching;

namespace PMT.Infrastructure.Caching;

/// <summary>
/// <see cref="ILookupCache"/> over the DI-registered <see cref="IMemoryCache"/>.
/// </summary>
/// <remarks>
/// Region invalidation is implemented with one <see cref="CancellationTokenSource"/> per
/// region, attached to every entry as an expiration token. Cancelling the source evicts the
/// whole region in O(1) without enumerating keys, which <see cref="IMemoryCache"/> does not
/// support directly.
/// </remarks>
public sealed class LookupCache(IMemoryCache cache) : ILookupCache, IDisposable
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _regions = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task<T> GetOrCreateAsync<T>(
        string region,
        string key,
        TimeSpan timeToLive,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);

        var cacheKey = string.Concat(region, "::", key);

        if (cache.TryGetValue(cacheKey, out T? hit) && hit is not null)
            return hit;

        // Two callers racing on a cold key can both run the factory. That is a repeat of a
        // read-only query which was about to run anyway, and it is cheaper than serialising
        // every cache miss behind a lock.
        var value = await factory(cancellationToken).ConfigureAwait(false);

        // A null result is not cached: the next caller should retry the factory rather than
        // be pinned to "nothing" for the whole TTL.
        if (value is not null)
        {
            var entryOptions = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = timeToLive };
            entryOptions.AddExpirationToken(new CancellationChangeToken(RegionToken(region)));
            cache.Set(cacheKey, value, entryOptions);
        }

        return value!;
    }

    /// <inheritdoc />
    public void Invalidate(string region)
    {
        if (string.IsNullOrWhiteSpace(region)) return;

        if (!_regions.TryRemove(region, out var source)) return;

        // Cancel before dispose: entries observe the cancelled token and are evicted. A
        // reader that grabbed this token concurrently gets an already-cancelled token, so its
        // entry expires immediately rather than being served stale.
        source.Cancel();
        source.Dispose();
    }

    private CancellationToken RegionToken(string region)
    {
        while (true)
        {
            var source = _regions.GetOrAdd(region, static _ => new CancellationTokenSource());
            try
            {
                return source.Token;
            }
            catch (ObjectDisposedException)
            {
                // Invalidate disposed this source between GetOrAdd and Token; drop it and retry.
                _regions.TryRemove(new KeyValuePair<string, CancellationTokenSource>(region, source));
            }
        }
    }

    /// <summary>Disposes the per-region token sources. Cached entries expire on their TTL.</summary>
    public void Dispose()
    {
        foreach (var source in _regions.Values) source.Dispose();
        _regions.Clear();
    }
}
