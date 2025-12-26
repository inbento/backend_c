using Lab1.BBL.Models;
using Lab1.DAL.Interfaces;
using Lab1.DAL.Models;
using Microsoft.Extensions.Options;
using Lab1.Config;
using Lab1.Services;
using Microsoft.Extensions.Logging;

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
                var messages = resultOrders.Select(order => new OmsOrderCreatedMessage
                {
                    Id = order.Id,
                    CustomerId = order.CustomerId,
                    DeliveryAddress = order.DeliveryAddress,
                    TotalPriceCents = order.TotalPriceCents,
                    TotalPriceCurrency = order.TotalPriceCurrency,
                    CreatedAt = order.CreatedAt,
                    OrderItems = order.OrderItems.Select(item => new OmsOrderItemMessage
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
                await _rabbitMqService.Publish(messages, _rabbitMqSettings.OrderCreatedQueue, token);
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

        private OrderUnit[] Map(V1OrderDal[] orders, ILookup<long, V1OrderItemDal> orderItemLookup = null)
        {
            return orders.Select(x => new OrderUnit
            {
                Id = x.Id,
                CustomerId = x.CustomerId,
                DeliveryAddress = x.DeliveryAddress,
                TotalPriceCents = x.TotalPriceCents,
                TotalPriceCurrency = x.TotalPriceCurrency,
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
