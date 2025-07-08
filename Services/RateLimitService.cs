using System.Collections.Concurrent;

namespace MeetAndGreet.API.Services
{
    public class RateLimitService
    {
        private readonly ConcurrentDictionary<string, DateTime> _userTimestamps = new ConcurrentDictionary<string, DateTime>();
        private readonly TimeSpan _rateLimitPeriod = TimeSpan.FromSeconds(1);
        private readonly int _messageLimit = 5;
        private readonly ILogger<RateLimitService> _logger;

        public RateLimitService(ILogger<RateLimitService> logger)
        {
            _logger = logger;
        }

        public bool IsAllowed(string userId)
        {
            if (string.IsNullOrEmpty(userId))
                throw new ArgumentException("User ID cannot be null or empty", nameof(userId));

            var now = DateTime.UtcNow;

            // Если пользователь новый, то разрешаем
            if (!_userTimestamps.ContainsKey(userId))
            {
                _userTimestamps.AddOrUpdate(userId, now, (key, oldValue) => now);
                return true;
            }

            // Если с момента последнего сообщения прошло больше времени, чем период ограничения, то разрешаем
            if (now - _userTimestamps[userId] > _rateLimitPeriod)
            {
                _userTimestamps.AddOrUpdate(userId, now, (key, oldValue) => now);
                return true;
            }

            // Считаем количество сообщений за период
            var messageCount = 0;
            foreach (var timestamp in _userTimestamps.Values)
            {
                if (now - timestamp <= _rateLimitPeriod)
                {
                    messageCount++;
                }
            }

            // Если количество сообщений превышает лимит, то запрещаем
            if (messageCount >= _messageLimit)
            {
                _logger.LogWarning($"Rate limit exceeded for user {userId}");
                return false;
            }

            // Иначе разрешаем
            _userTimestamps.AddOrUpdate(userId, now, (key, oldValue) => now);
            return true;
        }
    }
}
