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
        private IConnection _connection;
        private IModel _channel;
        private EventingBasicConsumer _consumer;

        public RabbitMQService(IConfiguration configuration, ILogger<RabbitMQService> logger)
        {
            _logger = logger;
            _hostname = configuration["RabbitMQ:Hostname"] ?? "localhost";
            _username = configuration["RabbitMQ:Username"] ?? "guest";
            _password = configuration["RabbitMQ:Password"] ?? "guest";

            _connectionFactory = new ConnectionFactory()
            {
                HostName = _hostname,
                UserName = _username,
                Password = _password,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
            };

            try
            {
                _connection = _connectionFactory.CreateConnection();
                _channel = _connection.CreateModel();

                _channel.ExchangeDeclare(exchange: _exchangeName, type: ExchangeType.Fanout, durable: true, autoDelete: false, arguments: null);
                _channel.QueueDeclare(queue: _queueName, durable: true, exclusive: false, autoDelete: false, arguments: null);
                _channel.QueueBind(queue: _queueName, exchange: _exchangeName, routingKey: "");

                _consumer = new EventingBasicConsumer(_channel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing RabbitMQ connection and channel");
                throw; // Re-throw the exception to prevent the service from starting
            }
        }

        public void PublishMessage<T>(T message)
        {
            try
            {
                var messageBody = JsonSerializer.Serialize(message);
                var body = Encoding.UTF8.GetBytes(messageBody);

                _channel.BasicPublish(exchange: _exchangeName, routingKey: "", basicProperties: null, body: body);
                _logger.LogInformation($"Published message to RabbitMQ: {messageBody}");
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
            // Dispose of resources
            if (_channel != null && _channel.IsOpen)
            {
                _channel.Close();
                _channel.Dispose();
                _logger.LogInformation("RabbitMQ channel closed");
            }

            if (_connection != null && _connection.IsOpen)
            {
                _connection.Close();
                _connection.Dispose();
                _logger.LogInformation("RabbitMQ connection closed");
            }
        }
    }
}