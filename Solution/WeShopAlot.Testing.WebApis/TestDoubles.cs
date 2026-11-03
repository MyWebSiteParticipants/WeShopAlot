using WeShopAlot.Data.Interfaces;
using WeShopAlot.Data.Models;
using WeShopAlot.Data.Repositories.Interfaces;

namespace WeShopAlot.Testing.WebApis
{
    /// <summary>
    /// Stand-ins for the two Redis-backed services, shared by both test factories so neither
    /// needs a Redis server. Everything else in the API is the real thing.
    /// </summary>
    internal static class TestDoubles
    {
        /// <summary>The [Cached] attribute calls this; nothing is ever cached, so every request hits the controller.</summary>
        internal sealed class NoResponseCache : IResponseCacheService
        {
            public Task CacheResponseAsync(string cacheKey, object response, TimeSpan timeToLive) => Task.CompletedTask;
            public Task<string> GetCachedResponse(string cacheKey) => Task.FromResult<string>(null);
        }

        /// <summary>The basket lives in Redis in production; here it is a dictionary for the life of the factory.</summary>
        internal sealed class InMemoryBasketRepository : IBasketRepository
        {
            private readonly Dictionary<string, CustomerBasket> baskets = new();
            public Task<CustomerBasket> GetBasketAsync(string basketId) => Task.FromResult(baskets.GetValueOrDefault(basketId));
            public Task<CustomerBasket> UpdateBasketAsync(CustomerBasket basket) { baskets[basket.Id] = basket; return Task.FromResult(basket); }
            public Task<bool> DeleteBasketAsync(string basketId) => Task.FromResult(baskets.Remove(basketId));
        }
    }
}
