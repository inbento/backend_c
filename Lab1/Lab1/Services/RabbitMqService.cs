using Common;
using Lab1.Config;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Lab1.Services
{
    public class RabbitMqService(IOptions<RabbitMqSettings> settings, ILogger<RabbitMqService> logger)
    {
        private readonly ConnectionFactory _factory = new() { HostName = settings.Value.HostName, Port = settings.Value.Port };
        private readonly ILogger<RabbitMqService> _logger = logger;

        public async Task Publish<T>(IEnumerable<T> enumerable, string queue, CancellationToken token)
        {
            await using var connection = await _factory.CreateConnectionAsync(token);
            await using var channel = await connection.CreateChannelAsync(cancellationToken: token);
            await channel.QueueDeclareAsync(
                queue: queue,
                durable: false,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: token);

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = null, // Изменено на null для использования PascalCase
                WriteIndented = false 
            };

            foreach (var message in enumerable)
            {
                var messageStr = JsonSerializer.Serialize(message, jsonOptions);
                _logger.LogInformation("Publishing message to RabbitMQ. Queue: {Queue}, Message: {Message}", queue, messageStr);
                var body = Encoding.UTF8.GetBytes(messageStr);
                await channel.BasicPublishAsync(
                    exchange: string.Empty,
                    routingKey: queue,
                    body: body,
                    cancellationToken: token);
            }
        }
    }
}

