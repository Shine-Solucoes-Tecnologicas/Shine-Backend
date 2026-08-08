using Microsoft.Extensions.Caching.Memory;

namespace Shine.Infrastructure;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    Task SetAsync<T>(string key, T value, CacheEntryOptions options, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}

public sealed record CacheEntryOptions(TimeSpan? AbsoluteExpiration = null, TimeSpan? SlidingExpiration = null);

public sealed class MemoryCacheService(IMemoryCache cache) : ICacheService
{
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(cache.TryGetValue(key, out T? value) ? value : default);

    public Task SetAsync<T>(string key, T value, CacheEntryOptions options, CancellationToken cancellationToken = default)
    {
        var memoryOptions = new MemoryCacheEntryOptions();
        if (options.AbsoluteExpiration is { } absolute)
            memoryOptions.AbsoluteExpirationRelativeToNow = absolute;
        if (options.SlidingExpiration is { } sliding)
            memoryOptions.SlidingExpiration = sliding;
        cache.Set(key, value, memoryOptions);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cache.Remove(key);
        return Task.CompletedTask;
    }
}
