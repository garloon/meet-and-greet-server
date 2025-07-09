using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace MeetAndGreet.API.Services
{
    public class RabbitMQService : IDisposable
    {
        private readonly string _hostname;
        private readonly string _username;
        private readonly string _password;
        private readonly ILogger<RabbitMQService> _logger;
        private readonly string _exchangeName = "chat_exchange";
        private readonly string _queueName = "chat_queue";
        private readonly IConnectionFactory _connectionFactory;
        private readonly IConnection _connection;
        private readonly IModel _channel;
        private readonly EventingBasicConsumer _consumer;
        private readonly AsyncRetryPolicy _retryPolicy;

        public RabbitMQService(IConfiguration configuration, ILogger<RabbitMQService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _hostname = configuration["RabbitMQ:Hostname"] ?? "localhost";
            _username = configuration["RabbitMQ:Username"] ?? "guest";
            _password = configuration["RabbitMQ:Password"] ?? "guest";
            
            _logger.LogInformation("RabbitMQService constructed.");

            _connectionFactory = new ConnectionFactory()
            {
                HostName = _hostname,
                UserName = _username,
                Password = _password,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
            };

            _retryPolicy = Policy
                .Handle<RabbitMQ.Client.Exceptions.BrokerUnreachableException>()
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    onRetry: (exception, timeSpan, retryAttempt, context) =>
                    {
                        _logger.LogWarning(exception, "Attempt {RetryAttempt} of {MaxRetries} failed. Retrying in {TimeSpan} seconds.", retryAttempt, 3, timeSpan.TotalSeconds);
                    }
                );

            try
            {
                _connection = _connectionFactory.CreateConnection();
                _channel = _connection.CreateModel();

                _channel.ExchangeDeclare(exchange: _exchangeName, type: ExchangeType.Fanout, durable: true, autoDelete: false, arguments: null);
                _channel.QueueDeclare(queue: _queueName, durable: true, exclusive: false, autoDelete: false, arguments: null);
                _channel.QueueBind(queue: _queueName, exchange: _exchangeName, routingKey: "");

                _consumer = new EventingBasicConsumer(_channel);
                _logger.LogInformation("RabbitMQ connected to {Hostname}", _hostname);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing RabbitMQ connection and channel");
                throw;
            }
        }

        public async Task PublishMessage<T>(T message)
        {
            try
            {
                var messageId = Guid.NewGuid();
                
                if (message is ChatMessage chatMessage)
                {
                    chatMessage.MessageId = messageId;

                    var messageBody = JsonSerializer.Serialize(chatMessage);
                    var body = Encoding.UTF8.GetBytes(messageBody);
                    
                    await _retryPolicy.ExecuteAsync(async () =>
                    {
                        _logger.LogDebug("Publishing message to RabbitMQ: {messageBody}", messageBody);
                        _channel.BasicPublish(exchange: _exchangeName, routingKey: "", basicProperties: null, body: body);
                        _logger.LogDebug("Message published to RabbitMQ.");
                    });
                }
                else
                {
                    _logger.LogWarning("Message type is not ChatMessage, unable to set MessageId.");
                    var messageBody = JsonSerializer.Serialize(message);
                    var body = Encoding.UTF8.GetBytes(messageBody);
                    
                    await _retryPolicy.ExecuteAsync(async () =>
                    {
                        _logger.LogDebug("Publishing message to RabbitMQ: {messageBody}", messageBody);
                        _channel.BasicPublish(exchange: _exchangeName, routingKey: "", basicProperties: null, body: body);
                        _logger.LogDebug("Message published to RabbitMQ.");
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error publishing message to RabbitMQ");
            }
        }

        public void ConsumeMessage<T>(Action<T> processMessage)
        {
            try
            {
                _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

                _consumer.Received += (model, ea) =>
                {
                    try
                    {
                        var body = ea.Body.ToArray();
                        var message = Encoding.UTF8.GetString(body);

                        T deserializedMessage = JsonSerializer.Deserialize<T>(message);

                        processMessage(deserializedMessage);

                        _channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing RabbitMQ message");
                        _channel.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: true);
                    }
                };

                _channel.BasicConsume(queue: _queueName, autoAck: false, consumer: _consumer);
                _logger.LogInformation("Consuming messages from RabbitMQ...");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error consuming messages from RabbitMQ");
            }
        }

        public void Dispose()
        {
            if (_channel != null && _channel.IsOpen)
            {
                try
                {
                    _channel.Close();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error closing RabbitMQ channel");
                }
                finally
                {
                    _channel.Dispose();
                }

                _logger.LogInformation("RabbitMQ channel closed");
            }

            if (_connection != null && _connection.IsOpen)
            {
                try
                {
                    _connection.Close();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error closing RabbitMQ connection");
                }
                finally
                {
                    _connection.Dispose();
                }

                _logger.LogInformation("RabbitMQ connection closed");
            }
        }
    }
}