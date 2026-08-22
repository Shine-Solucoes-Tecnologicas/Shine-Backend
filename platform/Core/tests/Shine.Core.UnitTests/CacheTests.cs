using Microsoft.Extensions.Caching.Memory;
using Shine.Infrastructure;

namespace Shine.UnitTests;

public sealed class CacheTests
{
    [Fact]
    public async Task Cache_supports_set_get_and_remove()
    {
        var service = new MemoryCacheService(new MemoryCache(new MemoryCacheOptions()));
        await service.SetAsync("key", "value", new CacheEntryOptions());

        Assert.Equal("value", await service.GetAsync<string>("key"));
        await service.RemoveAsync("key");
        Assert.Null(await service.GetAsync<string>("key"));
    }
}
