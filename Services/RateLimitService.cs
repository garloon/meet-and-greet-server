using System.Collections.Concurrent;

namespace MeetAndGreet.API.Services
{
    public class RateLimitService
    {
        private readonly ConcurrentDictionary<string, Queue<DateTime>> _userTimestamps = new ConcurrentDictionary<string, Queue<DateTime>>();
        private readonly TimeSpan _rateLimitPeriod = TimeSpan.FromSeconds(1);
        private readonly int _messageLimit = 5;
        private readonly ILogger<RateLimitService> _logger;

        public RateLimitService(ILogger<RateLimitService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogInformation("RateLimitService constructed.");
        }

        public bool IsAllowed(string userId)
        {
            if (string.IsNullOrEmpty(userId))
                throw new ArgumentException("User ID cannot be null or empty", nameof(userId));

            var now = DateTime.UtcNow;
            var timestamps = _userTimestamps.GetOrAdd(userId, (key) => new Queue<DateTime>());

            lock (timestamps)
            {
                while (timestamps.Count > 0 && now - timestamps.Peek() > _rateLimitPeriod)
                {
                    timestamps.Dequeue();
                }
                
                if (timestamps.Count >= _messageLimit)
                {
                    _logger.LogWarning("Rate limit exceeded for user {userId}", userId);
                    return false;
                }
                
                timestamps.Enqueue(now);
                return true;
            }
        }
    }
}