using Lab1.DAL.Models;

namespace Lab1.DAL.Interfaces
{
    public interface IOrderRepository
    {
        Task<V1OrderDal[]> BulkInsert(V1OrderDal[] model, CancellationToken token);

        Task<V1OrderDal[]> Query(QueryOrdersDalModel model, CancellationToken token);
        
        Task BulkUpdate(V1OrderDal[] model, CancellationToken token);
    }
}