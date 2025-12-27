using Consumer.Config;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Common;
using System.Text.Json;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Consumer.Base;

public abstract class BaseBatchMessageConsumer<T> : IHostedService
    where T : class
{
    private IConnection _connection;
    private IChannel _channel;
   
    private readonly ConnectionFactory _factory;
    private readonly RabbitMqSettings.TopicSettingsUnit _topicSettings;
    private readonly RabbitMqSettings _rabbitMqSettings;
    private readonly ILogger<BaseBatchMessageConsumer<T>> _logger;
    private List<MessageInfo> _messageBuffer;
    private System.Threading.Timer _batchTimer;
    private SemaphoreSlim _processingSemaphore;
   
    protected BaseBatchMessageConsumer(RabbitMqSettings rabbitMqSettings, Func<RabbitMqSettings, RabbitMqSettings.TopicSettingsUnit> getTopicSettings, ILoggerFactory loggerFactory)
    {
        _factory = new() { HostName = rabbitMqSettings.HostName, Port = rabbitMqSettings.Port };
        _rabbitMqSettings = rabbitMqSettings;
        _logger = loggerFactory.CreateLogger<BaseBatchMessageConsumer<T>>();

        _topicSettings = getTopicSettings(rabbitMqSettings);
        if (_topicSettings == null)
        {
            _logger.LogError("Topic settings for queue could not be loaded. Consumer will not start.");
            throw new InvalidOperationException("Topic settings are null."); // Throw to ensure service fails startup explicitly
        }
    }
   
    protected abstract Task ProcessMessages(T[] messages);
   
    public async Task StartAsync(CancellationToken token)
    {
        _logger.LogInformation("Starting consumer for queue: {QueueName}", _topicSettings.Queue);
        try
        {
            _connection = await _factory.CreateConnectionAsync(token);
            _channel = await _connection.CreateChannelAsync(cancellationToken: token);
            
            _messageBuffer = new List<MessageInfo>();
            _processingSemaphore = new SemaphoreSlim(1, 1);
            
            // Declare Dead Letter Exchange and Queue
            await _channel.ExchangeDeclareAsync(
                exchange: _rabbitMqSettings.DeadLetterExchange, // Changed
                type: "topic",
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: token);

            await _channel.QueueDeclareAsync(
                queue: _rabbitMqSettings.DeadLetterQueue, // Changed
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: token);

            await _channel.QueueBindAsync(
                queue: _rabbitMqSettings.DeadLetterQueue, // Changed
                exchange: _rabbitMqSettings.DeadLetterExchange, // Changed
                routingKey: _rabbitMqSettings.DeadLetterRoutingKey, // Changed
                arguments: null,
                cancellationToken: token);

            // Arguments for the main queue to direct failed messages to DLX
            var queueArguments = new Dictionary<string, object>
            {
                {"x-dead-letter-exchange", _rabbitMqSettings.DeadLetterExchange} // Changed
            };

            // Настройка prefetch для батчевой обработки
            await _channel.BasicQosAsync(0, (ushort)(_topicSettings.BatchSize * 2), false, token);
            
            var batchTimeout = TimeSpan.FromSeconds(_topicSettings.BatchTimeoutSeconds);
            // Таймер для принудительной обработки по времени
            _batchTimer = new System.Threading.Timer(ProcessBatchByTimeout, null, batchTimeout, batchTimeout);
            
            await _channel.QueueDeclareAsync(
                queue: _topicSettings.Queue, 
                durable: true, 
                exclusive: false,
                autoDelete: false,
                arguments: queueArguments, 
                cancellationToken: token);
            
            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += OnMessageReceived;
            
            await _channel.BasicConsumeAsync(queue: _topicSettings.Queue, autoAck: false, consumer: consumer, cancellationToken: token);
            _logger.LogInformation("Consumer for queue {QueueName} started successfully.", _topicSettings.Queue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Consumer for queue {QueueName} failed to start.", _topicSettings.Queue);
            // Optionally, you might want to rethrow the exception if it's a critical startup failure
            // throw;
        }
    }
    
    private async Task OnMessageReceived(object sender, BasicDeliverEventArgs ea)
    {
        await _processingSemaphore.WaitAsync();
        
        try
        {
            var message = Encoding.UTF8.GetString(ea.Body.ToArray());
            _messageBuffer.Add(new MessageInfo
            {
                Message = message,
                DeliveryTag = ea.DeliveryTag,
                ReceivedAt = DateTimeOffset.UtcNow
            });

            // Если достигли лимита батча - обрабатываем
            if (_messageBuffer.Count >= _topicSettings.BatchSize)
            {
                await ProcessBatch();
            }
        }
        finally
        {
            _processingSemaphore.Release();
        }
    }

    private async void ProcessBatchByTimeout(object state)
    {
        await _processingSemaphore.WaitAsync();
        
        try
        {
            if (_messageBuffer.Count > 0)
            {
                await ProcessBatch();
            }
        }
        finally
        {
            _processingSemaphore.Release();
        }
    }

    private async Task ProcessBatch()
    {
        if (_messageBuffer.Count == 0) return;

        var currentBatch = _messageBuffer.ToList();
        _messageBuffer.Clear();

        try
        {
            var messages = currentBatch.Select(x => x.Message.FromJson<T>()).ToArray();
            
            // Ваша логика обработки батча
            await ProcessMessages(messages);
            
            // ACK всех сообщений в батче (multiple = true для последнего)
            var lastDeliveryTag = currentBatch.Max(x => x.DeliveryTag);
            await _channel.BasicAckAsync(lastDeliveryTag, multiple: true);
            
            _logger.LogInformation($"Successfully processed batch of {currentBatch.Count} messages");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to process batch: {ex.Message}");
            
            // NACK всех сообщений в батче для повторной обработки
            var lastDeliveryTag = currentBatch.Max(x => x.DeliveryTag);
            await _channel.BasicNackAsync(lastDeliveryTag, multiple: true, requeue: false);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping consumer for queue: {QueueName}", _topicSettings.Queue);
        _batchTimer?.Dispose();
        _channel?.Dispose();
        _connection?.Dispose();
        _processingSemaphore?.Dispose();
        _logger.LogInformation("Consumer for queue {QueueName} stopped.", _topicSettings.Queue);
        return Task.CompletedTask;
    }
}