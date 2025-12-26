using Dapper;
using Lab1.DAL.Interfaces;
using Lab1.DAL.Models;
using Microsoft.Extensions.Logging;

namespace Lab1.DAL.Repositories
{
    public class AuditLogOrderRepository : IAuditLogOrderRepository
    {
        private readonly UnitOfWork _unitOfWork;
        private readonly ILogger<AuditLogOrderRepository> _logger;

        public AuditLogOrderRepository(UnitOfWork unitOfWork, ILogger<AuditLogOrderRepository> logger)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<V1AuditLogOrderDal[]> BulkInsert(V1AuditLogOrderDal[] auditLogs, CancellationToken token)
        {
            _logger.LogInformation("BulkInsert called with {AuditLogsCount} audit logs.", auditLogs.Length);

            if (auditLogs.Length == 0)
            {
                _logger.LogWarning("BulkInsert called with empty auditLogs array. Skipping insertion.");
                return Array.Empty<V1AuditLogOrderDal>();
            }

            try
            {
                var connection = await _unitOfWork.GetConnection(token);

                const string sql = @"
                    INSERT INTO audit_log_order 
                    (order_id, order_item_id, customer_id, order_status, created_at, updated_at)
                    SELECT 
                        order_id,
                        order_item_id,
                        customer_id,
                        order_status,
                        created_at,
                        updated_at
                    FROM unnest(@AuditLogs)
                    RETURNING 
                        id,
                        order_id,
                        order_item_id,
                        customer_id,
                        order_status,
                        created_at,
                        updated_at;
                ";

                // Используем bulk вставку через unnest для массива записей
                var results = await connection.QueryAsync<V1AuditLogOrderDal>(
                    new CommandDefinition(sql, new { AuditLogs = auditLogs }, cancellationToken: token));

                _logger.LogInformation("Successfully inserted {InsertedCount} audit logs.", results.Count());
                return results.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during bulk insert of audit logs.");
                throw; 
            }
        }

        public async Task<V1AuditLogOrderDal[]> Query(QueryAuditLogOrderDalModel model, CancellationToken token)
        {
            // Убеждаемся, что соединение инициализировано
            var connection = await _unitOfWork.GetConnection(token);

            var sql = @"
                SELECT * FROM audit_log_order 
                WHERE 1=1";

            var parameters = new DynamicParameters();

            if (model.Ids?.Length > 0)
            {
                sql += " AND id = ANY(@Ids)";
                parameters.Add("Ids", model.Ids);
            }

            if (model.OrderIds?.Length > 0)
            {
                sql += " AND order_id = ANY(@OrderIds)";
                parameters.Add("OrderIds", model.OrderIds);
            }

            if (model.CustomerIds?.Length > 0)
            {
                sql += " AND customer_id = ANY(@CustomerIds)";
                parameters.Add("CustomerIds", model.CustomerIds);
            }

            if (model.OrderStatuses?.Length > 0)
            {
                sql += " AND order_status = ANY(@OrderStatuses)";
                parameters.Add("OrderStatuses", model.OrderStatuses);
            }

            sql += " ORDER BY created_at DESC LIMIT @Limit OFFSET @Offset";
            parameters.Add("Limit", model.Limit);
            parameters.Add("Offset", model.Offset);

            var result = await connection.QueryAsync<V1AuditLogOrderDal>(
                new CommandDefinition(sql, parameters, cancellationToken: token));

            return result.ToArray();
        }
    }
}
