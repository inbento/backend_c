using Common;
using Lab1.Config;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Messages;

namespace Lab1.Services
{
    public class RabbitMqService(IOptions<RabbitMqSettings> settings) : IDisposable
    {
        private IConnection? _connection;
        private IChannel? _channel;
        
        public async Task Publish<T>(IEnumerable<T> enumerable, CancellationToken token)
            where T : BaseMessage
        {
            var channel = await Configure(token);

            foreach (var message in enumerable)
            {
                var messageStr = message.ToJson();
                var body = Encoding.UTF8.GetBytes(messageStr);
                await channel.BasicPublishAsync(
                    exchange: settings.Value.Exchange,
                    routingKey: message.RoutingKey,
                    body: body,
                    cancellationToken: token);
            }
        }

        private async Task<IChannel> Configure(CancellationToken token)
        {
            if (_channel?.IsOpen ?? false)
            {
                return _channel;
            }

            _connection = await new ConnectionFactory
                {
                    HostName = settings.Value.HostName,
                    Port = settings.Value.Port
                }
                .CreateConnectionAsync(token);

            _channel = await _connection.CreateChannelAsync(cancellationToken: token);

            // Declare Dead Letter Exchange and Queue
            await _channel.ExchangeDeclareAsync(
                exchange: settings.Value.DeadLetterExchange,
                type: "topic",
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: token);

            await _channel.QueueDeclareAsync(
                queue: settings.Value.DeadLetterQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: token);

            await _channel.QueueBindAsync(
                queue: settings.Value.DeadLetterQueue,
                exchange: settings.Value.DeadLetterExchange,
                routingKey: settings.Value.DeadLetterRoutingKey,
                arguments: null,
                cancellationToken: token);

            // Arguments for the main queue to direct failed messages to DLX
            var queueArguments = new Dictionary<string, object>
            {
                {"x-dead-letter-exchange", settings.Value.DeadLetterExchange}
            };

            await _channel.ExchangeDeclareAsync(exchange: settings.Value.Exchange, type: "topic", durable: true, autoDelete: false, arguments: null, cancellationToken: token);

            foreach (var mapping in settings.Value.ExchangeMappings)
            {
                await _channel.QueueDeclareAsync(
                    queue: mapping.Queue,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: queueArguments, // Added DLX arguments
                    cancellationToken: token);

                await _channel.QueueBindAsync(
                    queue: mapping.Queue,
                    exchange: settings.Value.Exchange,
                    routingKey: mapping.RoutingKeyPattern,
                    arguments: null,
                    cancellationToken: token);
            }

            return _channel;
        }

        public void Dispose()
        {
            _channel?.Dispose();
            _connection?.Dispose();
        }
    }
}