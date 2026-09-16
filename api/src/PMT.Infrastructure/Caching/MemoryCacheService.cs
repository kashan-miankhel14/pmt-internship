using Microsoft.Extensions.Caching.Memory;
using PMT.Application.Common.Interfaces;
namespace PMT.Infrastructure.Caching;
public sealed class MemoryCacheService(IMemoryCache cache) : ICacheService
{
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) => Task.FromResult(cache.TryGetValue(key, out T? value) ? value : default);
    public Task SetAsync<T>(string key, T value, TimeSpan duration, CancellationToken cancellationToken = default) { cache.Set(key, value, duration); return Task.CompletedTask; }
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default) { cache.Remove(key); return Task.CompletedTask; }
}
