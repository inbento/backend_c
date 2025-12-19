using WebApi.BLL.Models;
using WebApi.DAL;
using WebApi.DAL.Interfaces;
using WebApi.DAL.Models;

namespace WebApi.BLL.Services;

public class OrderService(UnitOfWork unitOfWork, IOrderRepository orderRepository, IOrderItemRepository orderItemRepository)
{
    /// <summary>
    /// Метод создания заказов
    /// </summary>
    public async Task<OrderUnit[]> BatchInsert(OrderUnit[] orderUnits, CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        await using var transaction = await unitOfWork.BeginTransactionAsync(token);

        try
        {
            var ordersToInsert = await orderRepository.BulkInsert(orderUnits.Select(o => new V1OrderDal
            {
                CustomerId = o.CustomerId,
                DeliveryAddress = o.DeliveryAddress,
                TotalPriceCents = o.TotalPriceCents,
                TotalPriceCurrency = o.TotalPriceCurrency,
                CreatedAt = now,
                UpdatedAt = now
            }).ToArray(), token);

            var orderMap =
                ordersToInsert.ToDictionary(x => (x.CustomerId, x.DeliveryAddress, x.TotalPriceCents, x.TotalPriceCurrency));
        
            foreach (var orderUnit in orderUnits)
            {
                orderUnit.Id = orderMap[(orderUnit.CustomerId, orderUnit.DeliveryAddress, orderUnit.TotalPriceCents, orderUnit.TotalPriceCurrency)].Id;
            }
        
            var orderItems = await orderItemRepository.BulkInsert(orderUnits.SelectMany(x => x.OrderItems.Select(a =>
                new V1OrderItemDal
                {
                    OrderId = x.Id,
                    ProductId = a.ProductId,
                    Quantity = a.Quantity,
                    ProductTitle = a.ProductTitle,
                    ProductUrl = a.ProductUrl,
                    PriceCents = a.PriceCents,
                    PriceCurrency = a.PriceCurrency,
                    CreatedAt = now,
                    UpdatedAt = now
                })).ToArray(), token);

            await transaction.CommitAsync(token);
        
            var orderItemLookup = orderItems.ToLookup(x => x.OrderId);
        
            return Map(ordersToInsert, orderItemLookup);
        }
        catch (Exception e) 
        {
            await transaction.RollbackAsync(token);
            throw;
        }
    }
    
    /// <summary>
    /// Метод получения заказов
    /// </summary>
    public async Task<OrderUnit[]> GetOrders(QueryOrderItemsModel model, CancellationToken token)
    {
        var orders = await orderRepository.Query(new QueryOrdersDalModel
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
            var orderItems = await orderItemRepository.Query(new QueryOrderItemsDalModel
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