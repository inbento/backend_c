using Common;
using Consumer.Base;
using Consumer.Config;
using Lab1.BBL.Models;
using Lab1.DAL.Models;
using Microsoft.Extensions.Options;
using Models.Dto.Common;
using System.Linq;
using Lab1.Clients;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Messages;
using System.Threading;

namespace Consumer.Consumers;

public class BatchOmsOrderCreatedConsumer(
    IOptions<RabbitMqSettings> rabbitMqSettings,
    IServiceProvider serviceProvider,
    ILogger<BatchOmsOrderCreatedConsumer> logger, // Keep this logger for this specific consumer's logs
    ILoggerFactory loggerFactory) // Add ILoggerFactory
    : BaseBatchMessageConsumer<OrderCreatedMessage>(rabbitMqSettings.Value, x => x.OrderCreated, loggerFactory) // Pass loggerFactory to base
{
    private static int _batchCounter = 0;

    protected override async Task ProcessMessages(OrderCreatedMessage[] messages)
    {
        Interlocked.Increment(ref _batchCounter);
        logger.LogInformation("Processing batch {BatchCounter} of {MessagesCount} messages.", _batchCounter, messages.Length);

        if (_batchCounter % 5 == 0)
        {
            logger.LogError("Simulating an error for batch {BatchCounter} to test DLX.", _batchCounter);
            throw new InvalidOperationException($"Simulated error for batch {_batchCounter}");
        }

        using var scope = serviceProvider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<OmsClient>();
        
        var ordersToLog = messages.Select(message => new V1AuditLogOrderRequest.LogOrder
        {
            OrderId = message.Id,
            CustomerId = message.CustomerId,
            OrderStatus = message.Status,
            OrderItemId = message.OrderItems.FirstOrDefault()?.Id > 0 ? message.OrderItems.FirstOrDefault()!.Id : 1 // Ensure positive OrderItemId
        }).ToArray();

        logger.LogInformation("Identified {OrdersToLogCount} orders to log.", ordersToLog.Length);

        if (ordersToLog.Any())
        {
            try
            {
                logger.LogInformation("Calling OmsClient.LogOrder with {OrdersCount} orders.", ordersToLog.Length);
                var response = await client.LogOrder(new V1AuditLogOrderRequest
                {
                    Orders = ordersToLog
                }, CancellationToken.None);
                logger.LogInformation("OmsClient.LogOrder call completed successfully. Response: {Response}", response.ToJson());
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error calling OmsClient.LogOrder for orders.");
            }
        }
        else
        {
            logger.LogInformation("No orders to log after filtering. Skipping OmsClient.LogOrder call.");
        }
    }
}