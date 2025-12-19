using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using UniverseLabs.Messages;
using UniverseLabs.Oms.Consumer.Clients;
using UniverseLabs.Oms.Consumer.Config;
using UniverseLabs.Oms.Consumer.Dto;

namespace UniverseLabs.Oms.Consumer.Consumers;

public class OmsOrderCreatedConsumer(IOptions<RabbitMqSettings> rabbitMqSettings, IServiceProvider serviceProvider) : IHostedService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly IOptions<RabbitMqSettings> _rabbitMqSettings = rabbitMqSettings;
    private readonly ConnectionFactory _factory = new() { HostName = rabbitMqSettings.Value.HostName, Port = rabbitMqSettings.Value.Port };
    private IConnection _connection;
    private IChannel _channel;
    private AsyncEventingBasicConsumer _consumer;
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _connection = await _factory.CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await _channel.QueueDeclareAsync(
            queue: _rabbitMqSettings.Value.OrderCreatedQueue, 
            durable: false, 
            exclusive: false,
            autoDelete: false,
            arguments: null, 
            cancellationToken: cancellationToken);

        _consumer = new AsyncEventingBasicConsumer(_channel);
        _consumer.ReceivedAsync += async (sender, args) =>
        {
            var body = args.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);
            var order = message.FromJson<OrderCreatedMessage>();

            Console.WriteLine("Received: " + message);
            
            using var scope = _serviceProvider.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<OmsClient>();
            await client.LogOrder(new V1AuditLogOrderRequest
            {
                Orders = order.OrderItems.Select(x => 
                    new V1AuditLogOrderRequest.LogOrder
                    {
                        OrderId = order.Id,
                        OrderItemId = x.Id,
                        CustomerId = order.CustomerId,
                        OrderStatus = "Created"
                    }).ToArray()
            }, CancellationToken.None);
        };
        
        await _channel.BasicConsumeAsync(
            queue: _rabbitMqSettings.Value.OrderCreatedQueue, 
            autoAck: true, 
            consumer: _consumer,
            cancellationToken: cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        _connection?.Dispose();
        _channel?.Dispose();
    }
}