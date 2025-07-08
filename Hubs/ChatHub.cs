using MeetAndGreet.API.Common;
using MeetAndGreet.API.Data;
using MeetAndGreet.API.Models;
using MeetAndGreet.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace MeetAndGreet.API.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly RedisService _redisService;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ChatHub> _logger;
        private readonly RateLimitService _rateLimitService;
        private readonly RabbitMQService _rabbitMQService;

        public ChatHub(
            RedisService redisService,
            IServiceProvider serviceProvider,
            ILogger<ChatHub> logger,
            RateLimitService rateLimitService,
            RabbitMQService rabbitMQService)
        {
            _redisService = redisService;
            _serviceProvider = serviceProvider;
            _logger = logger;
            _rateLimitService = rateLimitService;
            _rabbitMQService = rabbitMQService;
        }

        public async Task JoinChannel(string channelId, string userId, string userName)
        {
            if (string.IsNullOrEmpty(userId))
            {
                throw new ArgumentException("User ID cannot be empty");
            }

            if (string.IsNullOrEmpty(userName))
            {
                userName = $"Гость-{userId[..6]}";
            }

            await _redisService.RemoveUserFromAllChannels(userId, Context.ConnectionId);

            await _redisService.AddUserToChannel(channelId, userId, Context.ConnectionId, userName);
            await Groups.AddToGroupAsync(Context.ConnectionId, channelId);

            var onlineUsers = await _redisService.GetOnlineUsersWithNames(channelId);

            await Clients.Group(channelId).SendAsync(HubEvents.UserJoined, userName, onlineUsers);

            _logger.LogInformation("User {UserId} joined channel {ChannelId}", userId, channelId);
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            try
            {
                var userId = await _redisService.GetUserIdFromConnectionId(Context.ConnectionId);
                if (userId != null)
                {
                    var channelId = await _redisService.GetChannelIdForUser(userId);
                    if (channelId != null)
                    {
                        _logger.LogInformation("User {UserId} disconnected from channel {ChannelId}", userId, channelId);
                        await _redisService.RemoveUserFromChannel(channelId, userId, Context.ConnectionId);
                        await Groups.RemoveFromGroupAsync(Context.ConnectionId, channelId);

                        var onlineUsers = await _redisService.GetOnlineUsersInChannel(channelId);
                        await Clients.Group(channelId).SendAsync(HubEvents.UserLeft, userId, onlineUsers);
                    }
                    else
                    {
                        _logger.LogWarning("ChannelId not found for user {UserId} on disconnect", userId);
                    }

                    await _redisService.RemoveUserIdConnectionId(userId);
                }
                else
                {
                    _logger.LogWarning("UserId not found for connection id {ConnectionId} on disconnect", Context.ConnectionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while handling disconnect for connection id {ConnectionId}", Context.ConnectionId);
            }
            finally
            {
                await base.OnDisconnectedAsync(exception);
            }
        }

        public async Task LeaveChannel(string channelId, string userId)
        {
            _logger.LogInformation("User {UserId} leaving channel {ChannelId}", userId, channelId);

            await _redisService.RemoveUserFromChannel(channelId, userId, Context.ConnectionId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, channelId);

            var onlineUsers = await _redisService.GetOnlineUsersInChannel(channelId);
            await Clients.Group(channelId).SendAsync(HubEvents.UserLeft, userId, onlineUsers);

            _logger.LogInformation("User {UserId} successfully left channel {ChannelId}", userId, channelId);
        }

        public async Task ForceLeaveAllChannels(string userId)
        {
            var userChannels = await _redisService.GetUserChannels(userId);
            foreach (var channelId in userChannels)
            {
                await _redisService.RemoveUserFromChannel(channelId, userId, Context.ConnectionId);
                var onlineUsers = await _redisService.GetOnlineUsersInChannel(channelId);
                await Clients.Group(channelId).SendAsync(HubEvents.UserLeft, userId, onlineUsers);
            }
            await _redisService.RemoveUserIdConnectionId(userId);
        }

        public async Task SendMessage(string channelId, string user, string message)
        {
            if (!_rateLimitService.IsAllowed(Context.UserIdentifier))
            {
                _logger.LogWarning($"Rate limit exceeded for user {Context.UserIdentifier}");
                await Clients.Caller.SendAsync("ReceiveMessage", "System", "Превышен лимит сообщений. Пожалуйста, подождите.");
                return;
            }

            _logger.LogInformation("Received message from user {User} in channel {ChannelId}", user, channelId);

            try
            {
                if (string.IsNullOrWhiteSpace(message))
                    throw new HubException("Сообщение не может быть пустым.");

                if (message.Length > 100)
                    throw new HubException("Сообщение слишком длинное.");

                _rabbitMQService.PublishMessage(new
                {
                    ChannelId = channelId,
                    User = user,
                    Message = message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending message to channel {ChannelId}", channelId);
            }
        }

        public async Task SendHeartbeat()
        {
            var userId = await _redisService.GetUserIdFromConnectionId(Context.ConnectionId);
            if (userId != null)
            {
                await _redisService.UpdateUserActivity(userId, Context.ConnectionId);
            }
        }
    }
}
