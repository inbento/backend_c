namespace Consumer.Config;
using System.Text;
using System.Text.Json;

public class RabbitMqSettings
{
    public string HostName { get; set; }

    public int Port { get; set; }

    public TopicSettingsUnit OrderCreated { get; set; }

    public TopicSettingsUnit OrderStatusChanged { get; set; }

    public string DeadLetterExchange { get; set; }
    public string DeadLetterQueue { get; set; }
    public string DeadLetterRoutingKey { get; set; }

    public class TopicSettingsUnit
    {
        public string Queue { get; set; }

        public ushort BatchSize { get; set; }

        public int BatchTimeoutSeconds { get; set; }
    }
}