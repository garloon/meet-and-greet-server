using MeetAndGreet.API.Common;
using MeetAndGreet.API.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MeetAndGreet.API.Services
{
    public class ChatMessageConsumer : BackgroundService
    {
        private readonly ILogger<ChatMessageConsumer> _logger;
        private readonly IServiceProvider _serviceProvider;

        public ChatMessageConsumer(ILogger<ChatMessageConsumer> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ChatMessageConsumer is starting.");

            try
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var rabbitMqService = scope.ServiceProvider.GetRequiredService<RabbitMQService>();
                    var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<ChatHub>>();

                    // Start consuming messages
                    rabbitMqService.ConsumeMessage<ChatMessage>(async message =>
                    {
                        _logger.LogInformation($"Received message from RabbitMQ: ChannelId = {message.ChannelId}, User = {message.User}, Message = {message.Message}");
                        try
                        {
                            //  Send the message to SignalR clients
                            await hubContext.Clients.Group(message.ChannelId).SendAsync(HubEvents.ReceiveMessage, message.User, message.Message, stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error sending message to SignalR group {message.ChannelId}");
                        }
                    });

                    // Keep the service running until a cancellation signal is received
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
    }
}