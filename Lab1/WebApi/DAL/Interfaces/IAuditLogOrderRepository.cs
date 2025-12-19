using WebApi.DAL.Models;

public interface IAuditLogOrderRepository
{
    Task BulkInsert(V1AuditLogOrderDal[] model, CancellationToken token);
}