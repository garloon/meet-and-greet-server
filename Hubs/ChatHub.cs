using MeetAndGreet.API.Common;
using MeetAndGreet.API.Data;
using MeetAndGreet.API.Models;
using MeetAndGreet.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

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
            _redisService = redisService ?? throw new ArgumentNullException(nameof(redisService));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _rateLimitService = rateLimitService ?? throw new ArgumentNullException(nameof(rateLimitService));
            _rabbitMQService = rabbitMQService ?? throw new ArgumentNullException(nameof(rabbitMQService));

            _logger.LogInformation("ChatHub constructed.");
        }

        public async Task JoinChannel(string channelId, string userId, string userName, string avatarConfig)
        {
            _logger.LogInformation("User {UserId} attempting to join channel {ChannelId}.", userId, channelId);

            try
            {
                var currentChannel = await _redisService.GetChannelIdForUser(userId);

                if (!string.IsNullOrEmpty(currentChannel) && currentChannel != channelId)
                {
                    _logger.LogInformation("User {UserId} is switching from channel {CurrentChannel} to {ChannelId}.", userId, currentChannel, channelId);
                    await Clients.Group(currentChannel).SendAsync("UserLeft", userId);
                    await _redisService.RemoveUserFromChannel(currentChannel, userId, Context.ConnectionId);
                }


                await _redisService.AddUserToChannel(channelId, userId, Context.ConnectionId, userName, avatarConfig);
                await Groups.AddToGroupAsync(Context.ConnectionId, channelId);

                var onlineUsers = await _redisService.GetOnlineUsersWithNamesAndAvatars(channelId);
                await Clients.Group(channelId).SendAsync("UserJoined", userName, onlineUsers);
                _logger.LogInformation("User {UserId} successfully joined channel {ChannelId}.", userId, channelId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while joining channel {ChannelId} for user {UserId}.", channelId, userId);
                // DO NOT re-throw general exception to client!
            }
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            _logger.LogInformation("Connection {ConnectionId} disconnected.", Context.ConnectionId);

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

                    await _redisService.GetOrRemoveConnectionIdForUser(userId, true);
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
                _logger.LogInformation("Connection {ConnectionId} disconnect handling finished.", Context.ConnectionId);
            }
        }

        public async Task LeaveChannel(string channelId, string userId)
        {
            _logger.LogInformation("User {UserId} attempting to leave channel {ChannelId}.", userId, channelId);
            try
            {
                await _redisService.RemoveUserFromChannel(channelId, userId, Context.ConnectionId);
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, channelId);

                var onlineUsers = await _redisService.GetOnlineUsersInChannel(channelId);
                await Clients.Group(channelId).SendAsync("UserLeft", userId, onlineUsers);
                _logger.LogInformation("User {UserId} successfully left channel {ChannelId}.", userId, channelId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while leaving channel {ChannelId} for user {UserId}.", channelId, userId);
                // DO NOT re-throw general exception to client!
            }
        }

        public async Task ForceLeaveAllChannels(string userId)
        {
            _logger.LogInformation("ForceLeaveAllChannels called for user {UserId}.", userId);

            try
            {
                var userChannels = await _redisService.GetUserChannels(userId);
                foreach (var channelId in userChannels)
                {
                    await _redisService.RemoveUserFromChannel(channelId, userId, Context.ConnectionId);
                    var onlineUsers = await _redisService.GetOnlineUsersInChannel(channelId);
                    await Clients.Group(channelId).SendAsync(HubEvents.UserLeft, userId, onlineUsers);
                }
                await _redisService.GetOrRemoveConnectionIdForUser(userId, true);
                _logger.LogInformation("User {UserId} successfully forced to leave all channels.", userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while forcing user {UserId} to leave all channels.", userId);
                // DO NOT re-throw general exception to client!
            }
        }

        public async Task SendMessage(string channelId, string user, string message)
        {
            _logger.LogInformation("Received message from user {User} in channel {ChannelId}", user, channelId);

            if (!_rateLimitService.IsAllowed(Context.UserIdentifier))
            {
                _logger.LogWarning("Rate limit exceeded for user {UserIdentifier}", Context.UserIdentifier);
                await Clients.Caller.SendAsync("ReceiveMessage", "System", "Превышен лимит сообщений. Пожалуйста, подождите.");
                return;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(message))
                {
                    _logger.LogWarning("Received empty message from user {User} in channel {ChannelId}", user, channelId);
                    throw new HubException("Сообщение не может быть пустым.");
                }


                if (message.Length > 100)
                {
                    _logger.LogWarning("Received message too long from user {User} in channel {ChannelId}", user, channelId);
                    throw new HubException("Сообщение слишком длинное.");
                }

                await _rabbitMQService.PublishMessage(new
                {
                    ChannelId = channelId,
                    User = user,
                    Message = message
                });
            }
            catch (HubException hex)
            {
                _logger.LogWarning(hex, "HubException while sending message to channel {ChannelId} from user {User}: {Message}", channelId, user, hex.Message);
                throw; // Re-throw HubException so client receives the error.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending message to channel {ChannelId} from user {User}", channelId, user);
                // DO NOT re-throw general exception here as it might expose sensitive information.
                await Clients.Caller.SendAsync("ReceiveMessage", "System", "Произошла ошибка при отправке сообщения.");
            }
        }

        public async Task SendHeartbeat()
        {
            _logger.LogDebug("Received heartbeat from connection {ConnectionId}.", Context.ConnectionId);
            try
            {
                var userId = await _redisService.GetUserIdFromConnectionId(Context.ConnectionId);
                if (userId != null)
                {
                    await _redisService.UpdateUserActivity(userId, Context.ConnectionId);
                }
                else
                {
                    _logger.LogWarning("UserId not found for connection id {ConnectionId}.", Context.ConnectionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating user activity for connection {ConnectionId}.", Context.ConnectionId);
            }
        }
    }
}