using Lab1.BBL.Models;
using Lab1.DAL.Interfaces;
using Lab1.DAL.Models;
using Microsoft.Extensions.Logging;

namespace Lab1.BBL.Services
{
    public class AuditLogOrderService
    {
        private readonly IAuditLogOrderRepository _auditLogOrderRepository;
        private readonly ILogger<AuditLogOrderService> _logger;

        public AuditLogOrderService(IAuditLogOrderRepository auditLogOrderRepository, ILogger<AuditLogOrderService> logger)
        {
            _auditLogOrderRepository = auditLogOrderRepository;
            _logger = logger;
        }

        public async Task<V1AuditLogOrderResponse> CreateAuditLogs(V1AuditLogOrderRequest request, CancellationToken token)
        {
            _logger.LogInformation("CreateAuditLogs called with {OrdersCount} orders.", request.Orders.Length);

            if (request.Orders.Length == 0)
            {
                _logger.LogWarning("CreateAuditLogs called with an empty orders array. Skipping further processing.");
                return new V1AuditLogOrderResponse { AuditLogs = Array.Empty<V1AuditLogOrderResponse.AuditLogOrder>() };
            }

            var now = DateTimeOffset.UtcNow;

            var auditLogsDal = request.Orders.Select(order => new V1AuditLogOrderDal
            {
                OrderId = order.OrderId,
                OrderItemId = order.OrderItemId,
                CustomerId = order.CustomerId,
                OrderStatus = order.OrderStatus,
                CreatedAt = now,
                UpdatedAt = now
            }).ToArray();

            try
            {
                var savedLogs = await _auditLogOrderRepository.BulkInsert(auditLogsDal, token);

                _logger.LogInformation("Successfully processed {InsertedCount} audit logs in service.", savedLogs.Length);

                return new V1AuditLogOrderResponse
                {
                    AuditLogs = savedLogs.Select(log => new V1AuditLogOrderResponse.AuditLogOrder
                    {
                        Id = log.Id,
                        OrderId = log.OrderId,
                        OrderItemId = log.OrderItemId,
                        CustomerId = log.CustomerId,
                        OrderStatus = log.OrderStatus,
                        CreatedAt = log.CreatedAt,
                        UpdatedAt = log.UpdatedAt
                    }).ToArray()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during CreateAuditLogs in service.");
                throw;
            }
        }

        public async Task<V1AuditLogOrderResponse> GetAuditLogs(QueryAuditLogOrderModel model, CancellationToken token)
        {
            var dalModel = new QueryAuditLogOrderDalModel
            {
                Ids = model.Ids,
                OrderIds = model.OrderIds,
                CustomerIds = model.CustomerIds,
                OrderStatuses = model.OrderStatuses,
                Limit = model.PageSize,
                Offset = (model.Page - 1) * model.PageSize
            };

            var logs = await _auditLogOrderRepository.Query(dalModel, token);

            return new V1AuditLogOrderResponse
            {
                AuditLogs = logs.Select(log => new V1AuditLogOrderResponse.AuditLogOrder
                {
                    Id = log.Id,
                    OrderId = log.OrderId,
                    OrderItemId = log.OrderItemId,
                    CustomerId = log.CustomerId,
                    OrderStatus = log.OrderStatus,
                    CreatedAt = log.CreatedAt,
                    UpdatedAt = log.UpdatedAt
                }).ToArray()
            };
        }
    }

    public class QueryAuditLogOrderModel
    {
        public long[] Ids { get; set; }
        public long[] OrderIds { get; set; }
        public long[] CustomerIds { get; set; }
        public string[] OrderStatuses { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 100;
    }
}
