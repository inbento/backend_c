using Consumer.Base;
using Consumer.Config;
using Lab1.DAL.Models;
using Microsoft.Extensions.Options;
using Messages;
using Lab1.Clients;
using Microsoft.Extensions.Logging;

namespace Consumer.Consumers;

public class BatchOmsOrderStatusChangedConsumer(
    IOptions<RabbitMqSettings> rabbitMqSettings,
    IServiceProvider serviceProvider,
    ILogger<BatchOmsOrderStatusChangedConsumer> logger, // Keep this logger for this specific consumer's logs
    ILoggerFactory loggerFactory) // Add ILoggerFactory
    : BaseBatchMessageConsumer<OmsOrderStatusChangedMessage>(rabbitMqSettings.Value, x => x.OrderStatusChanged, loggerFactory) // Pass loggerFactory to base
{
    protected override async Task ProcessMessages(OmsOrderStatusChangedMessage[] messages)
    {
        logger.LogInformation("Processing batch of {MessagesCount} order status changed messages.", messages.Length);

        using var scope = serviceProvider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<OmsClient>();

        var ordersToLog = messages.Select(message => new V1AuditLogOrderRequest.LogOrder
            {
                OrderId = message.OrderId,
                CustomerId = message.CustomerId, // Populated from message
                OrderStatus = message.OrderStatus,
                OrderItemId = message.OrderItemId // Populated from message
            }).ToArray();

        logger.LogInformation("Identified {OrdersToLogCount} order status changes to log.", ordersToLog.Length);

        if (ordersToLog.Any())
        {
            try
            {
                logger.LogInformation("Calling OmsClient.LogOrder with {OrdersCount} order status changes.", ordersToLog.Length);
                await client.LogOrder(new V1AuditLogOrderRequest
                {
                    Orders = ordersToLog
                }, CancellationToken.None);
                logger.LogInformation("OmsClient.LogOrder call completed successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error calling OmsClient.LogOrder for order status changes.");
            }
        }
        else
        {
            logger.LogInformation("No order status changes to log. Skipping OmsClient.LogOrder call.");
        }
    }
}