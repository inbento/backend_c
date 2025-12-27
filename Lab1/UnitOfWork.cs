using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Data;
using Lab1.DAL.Models;

public class UnitOfWork(IOptions<DbSettings> dbSettings) : IDisposable
{
    private NpgsqlConnection _connection;

    public async Task<NpgsqlConnection> GetConnection(CancellationToken token)
    {
        if (_connection is not null)
        {
            return _connection;
        }

        var attempts = 0;
        var maxAttempts = 6;
        var delay = TimeSpan.FromSeconds(1);

        while (true)
        {
            attempts++;
            var dataSource = new NpgsqlDataSourceBuilder(dbSettings.Value.ConnectionString);

            // Регистрируем все композитные типы
            // Map both variants (with and without _dal suffix) to be compatible with migrations
            // and different naming used across the project.
            dataSource.MapComposite<V1OrderDal>("v1_order");
            dataSource.MapComposite<V1OrderDal>("v1_order_dal");
            dataSource.MapComposite<V1OrderItemDal>("v1_order_item");
            dataSource.MapComposite<V1OrderItemDal>("v1_order_item_dal");
            dataSource.MapComposite<V1AuditLogOrderDal>("v1_audit_log_order"); // Добавляем новый тип
            dataSource.MapComposite<V1AuditLogOrderDal>("v1_audit_log_order_dal");

            _connection = dataSource.Build().CreateConnection();
            _connection.StateChange += (sender, args) =>
            {
                if (args.CurrentState == ConnectionState.Closed)
                    _connection = null;
            };

            try
            {
                // Try to open; if backend types aren't ready this can fail — retry with backoff
                await _connection.OpenAsync(token);

                // Ensure composite types and type mappings are loaded from backend
                try
                {
                    _connection.ReloadTypes();
                }
                catch
                {
                    // If reload fails, we treat it as transient and allow retry below
                }

                return _connection;
            }
            catch (Exception ex) when (attempts < maxAttempts && (ex is Npgsql.NpgsqlException || ex is TimeoutException || ex is TaskCanceledException))
            {
                try { _connection?.Dispose(); } catch { }
                _connection = null;
                await Task.Delay(delay, token);
                // exponential backoff with cap
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 10));
                continue;
            }
            catch
            {
                try { _connection?.Dispose(); } catch { }
                _connection = null;
                throw;
            }
        }
    }

    public NpgsqlConnection Connection => _connection;
    public NpgsqlTransaction Transaction { get; private set; }

    public async ValueTask<NpgsqlTransaction> BeginTransactionAsync(CancellationToken token)
    {
        _connection ??= await GetConnection(token);
        Transaction = await _connection.BeginTransactionAsync(token);
        return Transaction;
    }

    public void Dispose()
    {
        Transaction?.Dispose();
        DisposeConnection();
        GC.SuppressFinalize(this);
    }

    ~UnitOfWork()
    {
        DisposeConnection();
    }

    private void DisposeConnection()
    {
        _connection?.Dispose();
        _connection = null;
    }
}
