using AutoFixture;
using Lab1.BBL.Models;
using Lab1.BBL.Services;
using Microsoft.Extensions.Hosting;
using System;
using Microsoft.Extensions.Logging;

namespace Lab1.Jobs
{
    public class OrderGenerator(IServiceProvider serviceProvider, ILogger<OrderGenerator> logger) : BackgroundService
    {
        private readonly Random _random = new Random();

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
                var fixture = new Fixture();

                // Wait for database readiness (composite types and migrations)
                try
                {
                    using var scope = serviceProvider.CreateScope();
                    var uow = scope.ServiceProvider.GetRequiredService<UnitOfWork>();

                    var start = DateTime.UtcNow;
                    var timeout = TimeSpan.FromSeconds(60);
                    while (!stoppingToken.IsCancellationRequested && DateTime.UtcNow - start < timeout)
                    {
                        try
                        {
                            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                            cts.CancelAfter(TimeSpan.FromSeconds(10));
                            var conn = await uow.GetConnection(cts.Token);
                            if (conn?.State == System.Data.ConnectionState.Open)
                            {
                                break;
                            }
                        }
                        catch
                        {
                            await Task.Delay(1000, stoppingToken);
                        }
                    }
                }
                catch
                {
                    // proceed anyway; individual DB ops will retry
                }
            
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = serviceProvider.CreateScope();
                    var orderService = scope.ServiceProvider.GetRequiredService<OrderService>();

                    var ordersToCreate = Enumerable.Range(1, 50)
                    .Select(_ =>
                    {
                        var orderItem = fixture.Build<OrderItemUnit>()
                            .With(x => x.Id, fixture.Create<long>() + 1) // Ensure positive Id
                            .With(x => x.PriceCurrency, "RUB")
                            .With(x => x.PriceCents, 1000)
                            .Create();

                        var order = fixture.Build<OrderUnit>()
                            .With(x => x.Id, fixture.Create<long>() + 1) // Ensure positive Id
                            .With(x => x.CustomerId, fixture.Create<long>() + 1) // Ensure positive CustomerId
                            .With(x => x.TotalPriceCurrency, "RUB")
                            .With(x => x.TotalPriceCents, 1000)
                            .With(x => x.OrderItems, fixture.CreateMany<OrderItemUnit>(1).ToArray()) // Ensure at least one item
                            .Create();

                        return order;
                    })
                    .ToArray();

                    await orderService.BatchInsert(ordersToCreate, stoppingToken);

                    // Randomly update status for some orders
                    var ordersToUpdate = ordersToCreate.Where(_ => _random.Next(0, 100) < 20).ToArray(); // Update ~20% of orders

                    if (ordersToUpdate.Any())
                    {
                        var possibleStatuses = new[] { "created", "processing", "completed", "cancelled" };
                        foreach (var order in ordersToUpdate)
                        {
                            try
                            {
                                var newStatus = possibleStatuses[_random.Next(possibleStatuses.Length)];
                                await orderService.UpdateOrdersStatus(new[] { order.Id }, newStatus, stoppingToken);
                            }
                            catch (Exception ex)
                            {
                                logger.LogError(ex, $"Failed to update status for order {order.Id}: {ex.Message}");
                            }
                        }
                    }

                    await Task.Delay(250, stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "OrderGenerator encountered an unhandled exception and might stop.");
                    await Task.Delay(1000, stoppingToken);
                }
            }
            logger.LogInformation("OrderGenerator background service is stopping.");
        }
    }
}