using StackExchange.Redis;

namespace MeetAndGreet.API.Services
{
    public class RedisService
    {
        private readonly ILogger<RedisService> _logger;
        private readonly IConnectionMultiplexer _redis;
        private readonly IDatabase _db;
        private readonly string _connectionString;

        public bool IsConnected => _redis?.IsConnected ?? false;

        public RedisService(IConfiguration configuration, ILogger<RedisService> logger)
        {
            _connectionString = configuration.GetConnectionString("Redis");
            _redis = ConnectionMultiplexer.Connect(_connectionString);
            _db = _redis.GetDatabase();
            _logger = logger;
        }

        public async Task RunCommandSafeAsync(Func<IDatabase, Task> action)
        {
            if (!IsConnected)
                throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis не подключен");

            try
            {
                await action(_redis.GetDatabase());
            }
            catch (RedisConnectionException ex)
            {
                _logger.LogError(ex, "Ошибка Redis");
                throw;
            }
        }

        public async Task<string> GetValueAsync(string key)
        {
            try
            {
                var db = _redis.GetDatabase();
                return await db.StringGetAsync(key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting value for key {Key}", key);
                return null;
            }
        }

        public async Task SetValueAsync(string key, string value)
        {
            try
            {
                var db = _redis.GetDatabase();
                await db.StringSetAsync(key, value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting value for key {Key}", key);
            }
        }

        public async Task AddUserToChannel(string channelId, string userId, string connectionId, string userName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userId))
                    throw new ArgumentException("User ID is required");

                if (string.IsNullOrWhiteSpace(userName))
                    userName = userId;

                var db = _redis.GetDatabase();
                var expiry = TimeSpan.FromHours(1);
                
                await _db.StringSetAsync($"user:{userId}:name", userName, expiry);
                await db.StringSetAsync($"user:{userId}:channel", channelId, expiry);
                await db.StringSetAsync($"connection:{connectionId}:user", userId, expiry);
                await db.HashSetAsync($"channel:{channelId}:users", userId, connectionId);
                await db.KeyExpireAsync($"channel:{channelId}:users", expiry);

                _logger.LogInformation("User {UserId} added to channel {ChannelId}", userId, channelId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding user {UserId} to channel {ChannelId}", userId, channelId);
            }
        }

        public async Task RemoveUserFromChannel(string channelId, string userId, string connectionId)
        {
            try
            {
                var db = _redis.GetDatabase();
                
                var transaction = db.CreateTransaction();

                _ = transaction.KeyDeleteAsync($"user:{userId}:channel");
                _ = transaction.KeyDeleteAsync($"connection:{connectionId}:user");
                _ = transaction.HashDeleteAsync($"channel:{channelId}:users", userId);

                await transaction.ExecuteAsync();

                _logger.LogInformation("User {UserId} completely removed from channel {ChannelId}", userId, channelId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing user {UserId} from channel {ChannelId}", userId, channelId);
            }
        }

        public async Task<string> GetUserIdFromConnectionId(string connectionId)
        {
            try
            {
                var db = _redis.GetDatabase();
                
                string userId = await db.StringGetAsync($"connection:{connectionId}:user");
                if (!string.IsNullOrEmpty(userId))
                {
                    return userId;
                }
                
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user id from connection id {ConnectionId}", connectionId);
                return null;
            }
        }

        public async Task<string> GetChannelIdForUser(string userId)
        {
            try
            {
                var db = _redis.GetDatabase();
                return await db.StringGetAsync($"user:{userId}:channel");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting channel id for user {UserId}", userId);
                return null;
            }
        }

        public async Task UpdateUserActivity(string userId, string connectionId)
        {
            try
            {
                var db = _redis.GetDatabase();
                var expiry = TimeSpan.FromMinutes(5);

                await db.KeyExpireAsync($"user:{userId}:channel", expiry);
                await db.KeyExpireAsync($"connection:{connectionId}:user", expiry);

                var channelId = await db.StringGetAsync($"user:{userId}:channel");
                if (!channelId.IsNullOrEmpty)
                {
                    await db.KeyExpireAsync($"channel:{channelId}:users", expiry);
                }
                _logger.LogDebug("Updated activity for user {UserId}, connection {ConnectionId}", userId, connectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating activity for user {UserId}, connection {ConnectionId}", userId, connectionId);
            }
        }

        public async Task<List<string>> GetOnlineUsersInChannel(string channelId)
        {
            try
            {
                var db = _redis.GetDatabase();
                var activeUsers = new List<string>();
                var hashEntries = await db.HashGetAllAsync($"channel:{channelId}:users");

                foreach (var entry in hashEntries)
                {
                    var userId = entry.Name.ToString();
                    var connectionId = entry.Value.ToString();
                    
                    bool isActive = await db.KeyExistsAsync($"connection:{connectionId}:user")
                                 && await db.KeyExistsAsync($"user:{userId}:channel");

                    if (isActive)
                    {
                        activeUsers.Add(userId);
                    }
                    else
                    {
                        await db.HashDeleteAsync($"channel:{channelId}:users", userId);
                    }
                }

                return activeUsers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting online users in channel {ChannelId}", channelId);
                return new List<string>();
            }
        }

        public async Task RemoveUserIdConnectionId(string userId)
        {
            try
            {
                var db = _redis.GetDatabase();

                // Construct the Redis key for the user's connection ID
                string key = $"user:{userId}:connectionId";

                // Delete the key from Redis
                bool removed = await db.KeyDeleteAsync(key);

                if (removed)
                {
                    _logger.LogInformation("Removed connection id mapping for user {UserId}", userId);
                }
                else
                {
                    _logger.LogWarning("No connection id mapping found for user {UserId}", userId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while removing connection id mapping for user {UserId}", userId);
            }
        }
        
        public async Task<List<string>> GetUserChannels(string userId)
        {
            try
            {
                var db = _redis.GetDatabase();
                var channelId = await db.StringGetAsync($"user:{userId}:channel");

                return string.IsNullOrEmpty(channelId)
                    ? new List<string>()
                    : new List<string> { channelId };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting channels for user {UserId}", userId);
                return new List<string>();
            }
        }

        public async Task<Dictionary<string, string>> GetOnlineUsersWithNames(string channelId)
        {
            var result = new Dictionary<string, string>();

            // Получаем все пары ключ-значение из хеша
            var hashEntries = await _db.HashGetAllAsync($"channel:{channelId}:users");

            foreach (var entry in hashEntries)
            {
                var userId = entry.Name.ToString();
                var name = await _db.StringGetAsync($"user:{userId}:name");

                if (!string.IsNullOrEmpty(name))
                {
                    result[userId] = name;
                }
                else
                {
                    // Если имя не найдено, используем ID как fallback
                    result[userId] = userId;
                }
            }

            return result;
        }

        public async Task RemoveUserFromAllChannels(string userId, string connectionId)
        {
            try
            {
                var channels = await GetUserChannels(userId);
                foreach (var channelId in channels)
                {
                    await RemoveUserFromChannel(channelId, userId, connectionId);
                }

                // Удаляем все связанные данные пользователя
                await _db.KeyDeleteAsync($"connection:{connectionId}:user");
                await _db.KeyDeleteAsync($"user:{userId}:channel");

                _logger.LogInformation("User {UserId} completely removed from all channels", userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing user {UserId} from all channels", userId);
            }
        }

        public async Task<bool> IsUserActive(string userId)
        {
            try
            {
                var db = _redis.GetDatabase();
                return await db.KeyExistsAsync($"user:{userId}:channel");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking user activity {UserId}", userId);
                return false;
            }
        }

        public async Task RunCleanupAsync()
        {
            while (true)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(5));
                    await CleanInactiveConnections();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in cleanup loop");
                }
            }
        }

        private async Task CleanInactiveConnections()
        {
            try
            {
                var db = _redis.GetDatabase();
                var server = _redis.GetServer(_redis.GetEndPoints().First());
                
                var connectionKeys = server.Keys(pattern: "connection:*:user");
                foreach (var key in connectionKeys)
                {
                    if (!await db.KeyExistsAsync(key))
                    {
                        var parts = key.ToString().Split(':');
                        var connectionId = parts[1];
                        var userId = await db.StringGetAsync(key);

                        if (!string.IsNullOrEmpty(userId))
                        {
                            var channelId = await db.StringGetAsync($"user:{userId}:channel");
                            if (!string.IsNullOrEmpty(channelId))
                            {
                                await RemoveUserFromChannel(channelId, userId, connectionId);
                            }
                        }
                    }
                }
                
                var channelKeys = server.Keys(pattern: "channel:*:users");
                foreach (var key in channelKeys)
                {
                    if (await db.HashLengthAsync(key) == 0)
                    {
                        await db.KeyDeleteAsync(key);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning inactive connections");
            }
        }

        private static IEnumerable<string> GetExistingChannelIds(IConnectionMultiplexer redis)
        {
            var endpoints = redis.GetEndPoints();
            foreach (var endpoint in endpoints)
            {
                var server = redis.GetServer(endpoint);
                foreach (var key in server.Keys(pattern: "channel:*:users"))
                {
                    yield return key.ToString().Split(':')[1];
                }
            }
        }
    }
}
