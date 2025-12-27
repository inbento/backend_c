using Lab1.BBL.Models;
using Lab1.DAL.Interfaces;
using Lab1.DAL.Models;
using Microsoft.Extensions.Options;
using Lab1.Config;
using Lab1.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using Messages;

namespace Lab1.BBL.Services
{
    public class OrderService
    {
        private readonly UnitOfWork _unitOfWork;
        private readonly IOrderRepository _orderRepository;
        private readonly IOrderItemRepository _orderItemRepository;
        private readonly RabbitMqService _rabbitMqService;
        private readonly RabbitMqSettings _rabbitMqSettings;
        private readonly ILogger<OrderService> _logger;

        public OrderService(
            UnitOfWork unitOfWork,
            IOrderRepository orderRepository,
            IOrderItemRepository orderItemRepository,
            RabbitMqService rabbitMqService, 
            IOptions<RabbitMqSettings> rabbitMqSettings,
            ILogger<OrderService> logger) 
        {
            _unitOfWork = unitOfWork;
            _orderRepository = orderRepository;
            _orderItemRepository = orderItemRepository;
            _rabbitMqService = rabbitMqService;
            _rabbitMqSettings = rabbitMqSettings.Value;
            _logger = logger;
        }

        /// <summary>
        /// Метод создания заказов
        /// </summary>
        public async Task<OrderUnit[]> BatchInsert(OrderUnit[] orderUnits, CancellationToken token)
        {
            _logger.LogInformation("BatchInsert called with {OrderUnitsCount} order units.", orderUnits.Length);
            var now = DateTimeOffset.UtcNow;
            List<OrderUnit> resultOrders;

            // Выполняем операции с БД в транзакции
            await using (var transaction = await _unitOfWork.BeginTransactionAsync(token))
            {
                try
                {
                    resultOrders = new List<OrderUnit>();

                    // 1. Подготавливаем и сохраняем заказы пакетно
                    var ordersDal = orderUnits.Select(orderUnit => new V1OrderDal
                    {
                        CustomerId = orderUnit.CustomerId,
                        DeliveryAddress = orderUnit.DeliveryAddress,
                        TotalPriceCents = orderUnit.TotalPriceCents,
                        TotalPriceCurrency = orderUnit.TotalPriceCurrency,
                        Status = orderUnit.Status ?? "created", // Added
                        CreatedAt = now,
                        UpdatedAt = now
                    }).ToArray();

                    var savedOrders = await _orderRepository.BulkInsert(ordersDal, token);

                    // 2. Подготавливаем и сохраняем позиции заказов пакетно
                    var allOrderItems = new List<V1OrderItemDal>();

                    for (int i = 0; i < savedOrders.Length; i++)
                    {
                        if (orderUnits[i].OrderItems?.Length > 0)
                        {
                            var orderItems = orderUnits[i].OrderItems.Select(item => new V1OrderItemDal
                            {
                                OrderId = savedOrders[i].Id,
                                ProductId = item.ProductId,
                                Quantity = item.Quantity,
                                ProductTitle = item.ProductTitle,
                                ProductUrl = item.ProductUrl,
                                PriceCents = item.PriceCents,
                                PriceCurrency = item.PriceCurrency,
                                CreatedAt = now,
                                UpdatedAt = now
                            });

                            allOrderItems.AddRange(orderItems);
                        }
                    }

                    var savedOrderItems = allOrderItems.Count > 0
                        ? await _orderItemRepository.BulkInsert(allOrderItems.ToArray(), token)
                        : [];

                    // 3. Группируем позиции по OrderId для удобства
                    var orderItemsByOrderId = savedOrderItems.GroupBy(x => x.OrderId)
                        .ToDictionary(g => g.Key, g => g.ToArray());

                    // 4. Собираем результат
                    for (int i = 0; i < savedOrders.Length; i++)
                    {
                        var savedOrder = savedOrders[i];
                        var orderItems = orderItemsByOrderId.GetValueOrDefault(savedOrder.Id) ?? [];

                        resultOrders.Add(new OrderUnit
                        {
                            Id = savedOrder.Id,
                            CustomerId = savedOrder.CustomerId,
                            DeliveryAddress = savedOrder.DeliveryAddress,
                            TotalPriceCents = savedOrder.TotalPriceCents,
                            TotalPriceCurrency = savedOrder.TotalPriceCurrency,
                            Status = savedOrder.Status, // Added
                            CreatedAt = savedOrder.CreatedAt,
                            UpdatedAt = savedOrder.UpdatedAt,
                            OrderItems = orderItems.Select(item => new OrderItemUnit
                            {
                                Id = item.Id,
                                OrderId = item.OrderId,
                                ProductId = item.ProductId,
                                Quantity = item.Quantity,
                                ProductTitle = item.ProductTitle,
                                ProductUrl = item.ProductUrl,
                                PriceCents = item.PriceCents,
                                PriceCurrency = item.PriceCurrency,
                                CreatedAt = item.CreatedAt,
                                UpdatedAt = item.UpdatedAt
                            }).ToArray()
                        });
                    }

                    // Коммитим транзакцию ДО отправки в RabbitMQ
                    await transaction.CommitAsync(token);
                }
                catch
                {
                    // Rollback только если транзакция ещё активна
                    // await using автоматически делает Dispose, который выполнит rollback если нужно
                    throw;
                }
            } // Транзакция завершена здесь

            // 5. После успешного commit отправляем в RabbitMQ (вне транзакции)
            try
            {
                var messages = resultOrders.Select(order => new Messages.OrderCreatedMessage
                {
                    Id = order.Id,
                    CustomerId = order.CustomerId,
                    DeliveryAddress = order.DeliveryAddress,
                    TotalPriceCents = order.TotalPriceCents,
                    TotalPriceCurrency = order.TotalPriceCurrency,
                    Status = order.Status, // Added
                    CreatedAt = order.CreatedAt,
                    OrderItems = order.OrderItems.Select(item => new Messages.OmsOrderItemMessage
                    {
                        Id = item.Id,
                        OrderId = item.OrderId,
                        ProductId = item.ProductId,
                        Quantity = item.Quantity,
                        ProductTitle = item.ProductTitle,
                        ProductUrl = item.ProductUrl,
                        PriceCents = item.PriceCents,
                        PriceCurrency = item.PriceCurrency,
                        CreatedAt = item.CreatedAt
                    }).ToArray()
                }).ToArray();

                _logger.LogInformation("Preparing to publish {MessagesCount} messages to RabbitMQ.", messages.Length);
                foreach (var message in messages)
                {
                    _logger.LogInformation("Message OrderId: {OrderId}, OrderItems count: {OrderItemsCount}", message.Id, message.OrderItems.Length);
                }

                // 6. Отправляем сообщения в RabbitMQ
                await _rabbitMqService.Publish(messages, token);
                _logger.LogInformation("Successfully published messages to RabbitMQ.");
            }
            catch (Exception ex)
            {
                // Логируем ошибку отправки в RabbitMQ, но данные уже в БД
                // В продакшене здесь нужно добавить retry logic или dead letter queue
                // throw; // Раскомментируйте если хотите чтобы ошибка RabbitMQ прерывала весь процесс
            }

            return resultOrders.ToArray();
        }

        /// <summary>
        /// Метод получения заказов
        /// </summary>
        public async Task<OrderUnit[]> GetOrders(QueryOrderItemsModel model, CancellationToken token)
        {
            var orders = await _orderRepository.Query(new QueryOrdersDalModel
            {
                Ids = model.Ids,
                CustomerIds = model.CustomerIds,
                Limit = model.PageSize,
                Offset = (model.Page - 1) * model.PageSize
            }, token);

            if (orders.Length is 0)
            {
                return [];
            }

            ILookup<long, V1OrderItemDal> orderItemLookup = null;
            if (model.IncludeOrderItems)
            {
                var orderItems = await _orderItemRepository.Query(new QueryOrderItemsDalModel
                {
                    OrderIds = orders.Select(x => x.Id).ToArray(),
                }, token);

                orderItemLookup = orderItems.ToLookup(x => x.OrderId);
            }

            return Map(orders, orderItemLookup);
        }

        public async Task UpdateOrdersStatus(long[] orderIds, string newStatus, CancellationToken token)
        {
            _logger.LogInformation("UpdateOrdersStatus called for {OrderIdsCount} orders with new status {NewStatus}", orderIds.Length, newStatus);

            var ordersToUpdate = (await _orderRepository.Query(new QueryOrdersDalModel { Ids = orderIds }, token)).ToList();

            if (ordersToUpdate.Count == 0)
            {
                _logger.LogInformation("No orders found for the given OrderIds. Returning without updates.");
                return;
            }

            var now = DateTimeOffset.UtcNow;
            List<long> updatedOrderIds = new List<long>();

            foreach (var order in ordersToUpdate)
            {
                // Простейшая стейт-машина
                if (order.Status == "created" && newStatus == "completed")
                {
                    throw new InvalidOperationException($"Cannot change order {order.Id} status from '{order.Status}' to '{newStatus}'.");
                }
                // Добавьте другие правила переходов статусов здесь, если необходимо

                order.Status = newStatus;
                order.UpdatedAt = now;
                updatedOrderIds.Add(order.Id);
            }

            await _orderRepository.BulkUpdate(ordersToUpdate.ToArray(), token);
            _logger.LogInformation("Successfully updated statuses for {UpdatedOrderCount} orders in DB.", updatedOrderIds.Count);

            // Публикация события изменения статуса в RabbitMQ
            try
            {
                var messages = ordersToUpdate.Select(order => new Messages.OmsOrderStatusChangedMessage 
                {
                    OrderId = order.Id,
                    OrderStatus = order.Status,
                    CustomerId = order.CustomerId, // Populated from OrderDal
                    OrderItemId = 1 // Default to 1 as it's a general order status change, not item specific
                }).ToArray();

                _logger.LogInformation("Preparing to publish {MessagesCount} order status changed messages to RabbitMQ.", messages.Length);
                await _rabbitMqService.Publish(messages, token); 
                _logger.LogInformation("Successfully published order status changed messages to RabbitMQ.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish order status changed messages to RabbitMQ.");
            }
        }

        private OrderUnit[] Map(V1OrderDal[] orders, ILookup<long, V1OrderItemDal> orderItemLookup = null)
        {
            return orders.Select(x => new OrderUnit
            {
                Id = x.Id,
                CustomerId = x.CustomerId,
                DeliveryAddress = x.DeliveryAddress,
                TotalPriceCents = x.TotalPriceCents,
                TotalPriceCurrency = x.TotalPriceCurrency,
                Status = x.Status, // Added
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
                OrderItems = orderItemLookup?[x.Id].Select(o => new OrderItemUnit
                {
                    Id = o.Id,
                    OrderId = o.OrderId,
                    ProductId = o.ProductId,
                    Quantity = o.Quantity,
                    ProductTitle = o.ProductTitle,
                    ProductUrl = o.ProductUrl,
                    PriceCents = o.PriceCents,
                    PriceCurrency = o.PriceCurrency,
                    CreatedAt = o.CreatedAt,
                    UpdatedAt = o.UpdatedAt
                }).ToArray() ?? []
            }).ToArray();
        }
    }
}