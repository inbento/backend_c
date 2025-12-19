using WebApi.DAL.Interfaces;
using WebApi.DAL.Models;

namespace WebApi.BLL.Services;

public class AuditLogOrderService(IAuditLogOrderRepository auditLogOrderRepository)
{
    public async Task LogOrders(V1AuditLogOrderDal[] logs, CancellationToken token)
    {
        await auditLogOrderRepository.BulkInsert(logs, token);
    }
}