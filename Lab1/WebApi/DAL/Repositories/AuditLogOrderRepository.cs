using Dapper;
using WebApi.DAL;
using WebApi.DAL.Interfaces;
using WebApi.DAL.Models;

namespace WebApi.DAL.Repositories;

public class AuditLogOrderRepository(UnitOfWork unitOfWork) : IAuditLogOrderRepository
{
    public async Task BulkInsert(V1AuditLogOrderDal[] models, CancellationToken token)
    {
        var sql = @"
            insert into audit_log_order 
            (
                order_id,
                order_item_id,
                customer_id,
                order_status,
                created_at,
                updated_at
             )
            select 
                order_id,
                order_item_id,
                customer_id,
                order_status,
                created_at,
                updated_at
            from unnest(@AuditLogs)
        ";

        var conn = await unitOfWork.GetConnection(token);
        await conn.ExecuteAsync(new CommandDefinition(
            sql, new { AuditLogs = models }, cancellationToken: token));
    }
}