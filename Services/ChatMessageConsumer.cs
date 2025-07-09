using MeetAndGreet.API.Common;
using MeetAndGreet.API.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace MeetAndGreet.API.Services
{
    public class ChatMessageConsumer : BackgroundService
    {
        private readonly ILogger<ChatMessageConsumer> _logger;
        private readonly RedisService _redisService;
        private readonly IServiceScopeFactory _scopeFactory;

        public ChatMessageConsumer(ILogger<ChatMessageConsumer> logger, RedisService redisService, IServiceScopeFactory scopeFactory)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _redisService = redisService ?? throw new ArgumentNullException(nameof(redisService));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger.LogInformation("ChatMessageConsumer constructed.");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ChatMessageConsumer is starting.");

            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var rabbitMqService = scope.ServiceProvider.GetRequiredService<RabbitMQService>();
                    var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<ChatHub>>();
                    
                    rabbitMqService.ConsumeMessage<ChatMessage>(async message =>
                    {
                        string messageKey = $"message:{message.MessageId}:processed";
                        
                        if (await _redisService.GetValueAsync(messageKey) != null)
                        {
                            _logger.LogWarning("Duplicate message detected: {MessageId},  Message Text: {Message},  User: {User}", message.MessageId, message.Message, message.User); // Log that we've seen this message before
                            return;
                        }

                        _logger.LogInformation($"Received message from RabbitMQ: ChannelId = {message.ChannelId}, User = {message.User}, Message = {message.Message}, MessageId = {message.MessageId}");
                        try
                        {
                            await hubContext.Clients.Group(message.ChannelId).SendAsync(HubEvents.ReceiveMessage, message.User, message.Message, stoppingToken);
                            
                            await _redisService.SetValueAsync(messageKey, "true");
                            
                            var expiry = TimeSpan.FromDays(1);
                            await _redisService.RunCommandSafeAsync(async db => await db.KeyExpireAsync(messageKey, expiry));
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error sending message to SignalR group {message.ChannelId}");
                        }
                    });
                    
                    await Task.Delay(Timeout.Infinite, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ChatMessageConsumer: {Message}", ex.Message);
            }

            _logger.LogInformation("ChatMessageConsumer is stopping.");
        }
    }

    public class ChatMessage
    {
        public string ChannelId { get; set; }
        public string User { get; set; }
        public string Message { get; set; }
        public Guid MessageId { get; set; }
    }
}