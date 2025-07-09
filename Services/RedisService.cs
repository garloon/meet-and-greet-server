using StackExchange.Redis;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MeetAndGreet.API.Services
{
    public class RedisService
    {
        private readonly ILogger<RedisService> _logger;
        private readonly IConnectionMultiplexer _redis;
        private readonly IDatabase _db;
        private readonly string _connectionString;

        private string GetUserChannelKey(string userId) => $"user:{userId}:channel";
        private string GetConnectionUserKey(string connectionId) => $"connection:{connectionId}:user";
        private string GetChannelUsersKey(string channelId) => $"channel:{channelId}:users";
        private string GetUserNameKey(string userId) => $"user:{userId}:name";
        private string GetUserConnectionKey(string userId) => $"user:{userId}:connection";

        public bool IsConnected => _redis?.IsConnected ?? false;

        public RedisService(IConfiguration configuration, ILogger<RedisService> logger)
        {
            _connectionString = configuration.GetConnectionString("Redis");
            var config = ConfigurationOptions.Parse(_connectionString);
            try
            {
                config.Password = Environment.GetEnvironmentVariable("Redis__Password") ?? throw new InvalidOperationException("Redis__Password environment variable not set.");
                _redis = ConnectionMultiplexer.Connect(config);
                _db = _redis.GetDatabase();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Redis Connect exception");
                throw;
            }


            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogInformation("RedisService constructed.");
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

        public async Task AddUserToChannel(string channelId, string userId, string connectionId, string userName, string avatarConfig)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userId))
                    throw new ArgumentException("User ID is required");

                if (string.IsNullOrWhiteSpace(userName))
                    userName = userId;

                var expiry = TimeSpan.FromHours(1);

                var userData = new
                {
                    name = userName,
                    avatar = avatarConfig
                };

                await _db.StringSetAsync(GetUserNameKey(userId), userName, expiry);
                await _db.StringSetAsync(GetUserChannelKey(userId), channelId, expiry);
                await _db.StringSetAsync(GetConnectionUserKey(connectionId), userId, expiry);
                await _db.HashSetAsync(GetChannelUsersKey(channelId), userId, connectionId);
                await _db.KeyExpireAsync(GetChannelUsersKey(channelId), expiry);
                await _db.HashSetAsync(GetChannelUsersKey(channelId), userId, JsonSerializer.Serialize(userData));

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

                await transaction.KeyDeleteAsync($"user:{userId}:channel");
                await transaction.KeyDeleteAsync($"connection:{connectionId}:user");
                await transaction.HashDeleteAsync($"channel:{channelId}:users", userId);

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

        public async Task<Dictionary<string, string>> GetOnlineUsersWithNamesAndAvatars(string channelId)
        {
            var db = _redis.GetDatabase();
            var hashEntries = await db.HashGetAllAsync($"channel:{channelId}:users");

            return hashEntries.ToDictionary(
                x => x.Name.ToString(),
                x => x.Value.ToString()
            );
        }

        public async Task<string?> GetOrRemoveConnectionIdForUser(string userId, bool remove = false)
        {
            string key = $"user:{userId}:connection";

            try
            {
                var db = _redis.GetDatabase();
                var connectionId = await db.StringGetAsync(key);

                if (connectionId.HasValue)
                {
                    if (remove)
                    {
                        await db.KeyDeleteAsync(key);
                        _logger.LogInformation("Removed connection id mapping for user {UserId}", userId);
                        return null;
                    }

                    _logger.LogDebug("ConnectionId {connectionId} found in Redis for user {UserId}", connectionId, userId);
                    return connectionId.ToString();
                }

                _logger.LogWarning("No connection id mapping found for user {UserId}", userId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting/removing connection id for user {UserId}", userId);
                return null;
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

        public async Task UpdateUserConnection(string userId, string connectionId)
        {
            try
            {
                var db = _redis.GetDatabase();
                await db.StringSetAsync($"user:{userId}:connection", connectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating connection for user {UserId}", userId);
            }
        }
    }
}