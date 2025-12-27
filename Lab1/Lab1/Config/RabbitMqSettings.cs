namespace Lab1.Config;
using System.Text;
using System.Text.Json;

public class RabbitMqSettings
{
    public string HostName { get; set; }
    public int Port { get; set; }

    public string Exchange { get; set; }
    
    public ExchangeMapping[] ExchangeMappings { get; set; }
    
    public string DeadLetterExchange { get; set; }
    public string DeadLetterQueue { get; set; }
    public string DeadLetterRoutingKey { get; set; }
    
    public class ExchangeMapping
    {
        public string Queue { get; set; }
        
        public string RoutingKeyPattern { get; set; }
    }
}