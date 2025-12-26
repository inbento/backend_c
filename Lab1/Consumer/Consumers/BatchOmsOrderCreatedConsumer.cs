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

namespace Consumer.Consumers;

public class BatchOmsOrderCreatedConsumer(
    IOptions<RabbitMqSettings> rabbitMqSettings,
    IServiceProvider serviceProvider,
    ILogger<BatchOmsOrderCreatedConsumer> logger)
    : BaseBatchMessageConsumer<OmsOrderCreatedMessage>(rabbitMqSettings.Value)
{
    protected override async Task ProcessMessages(OmsOrderCreatedMessage[] messages)
    {
        logger.LogInformation("Processing batch of {MessagesCount} messages.", messages.Length);

        using var scope = serviceProvider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<OmsClient>();
        
        var ordersToLog = messages.Where(order => order.OrderItems != null && order.OrderItems.Any())
            .SelectMany(order => order.OrderItems.Select(ol => 
            new V1AuditLogOrderRequest.LogOrder
            {
                OrderId = order.Id,
                OrderItemId = ol.Id,
                CustomerId = order.CustomerId,
                OrderStatus = nameof(OrderStatus.Created)
            })).ToArray();

        logger.LogInformation("Identified {OrdersToLogCount} orders to log after filtering.", ordersToLog.Length);

        if (ordersToLog.Any())
        {
            try
            {
                logger.LogInformation("Calling OmsClient.LogOrder with {OrdersCount} orders.", ordersToLog.Length);
                await client.LogOrder(new V1AuditLogOrderRequest
                {
                    Orders = ordersToLog
                }, CancellationToken.None);
                logger.LogInformation("OmsClient.LogOrder call completed successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error calling OmsClient.LogOrder.");
            }
        }
        else
        {
            logger.LogInformation("No orders to log after filtering. Skipping OmsClient.LogOrder call.");
        }
    }
}

