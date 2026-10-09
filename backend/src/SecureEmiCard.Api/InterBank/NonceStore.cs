using Microsoft.Extensions.Caching.Memory;

namespace SecureEmiCard.Api.InterBank
{
    public interface INonceStore
    {
        /// <summary>
        /// Records the nonce and returns true if it was NOT seen before (within the time-to-live).
        /// Returns false for a replay.
        /// </summary>
        bool TryUse(string partnerId, string nonce, TimeSpan timeToLive);
    }

    /// <summary>
    /// In-memory nonce store. Correct for ONE API instance. With several instances behind a load balancer,
    /// a replay sent to another instance would not be detected - use a shared store (Redis SET NX with
    /// expiry, or a table with a unique index) instead. Module 12 (deployment) covers this.
    /// </summary>
    public class MemoryNonceStore : INonceStore
    {
        private readonly IMemoryCache _cache;
        private readonly object _lock = new();

        public MemoryNonceStore(IMemoryCache cache) => _cache = cache;

        public bool TryUse(string partnerId, string nonce, TimeSpan timeToLive)
        {
            var key = $"interbank-nonce:{partnerId}:{nonce}";
            // "check then add" must be atomic, otherwise two copies of the same request arriving
            // at the same moment could both pass the check.
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out _)) return false;
                _cache.Set(key, true, timeToLive);
                return true;
            }
        }
    }
}
