using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Messages
{
    public class OrderCreatedMessage : BaseMessage
    {
        public long Id { get; set; }

        public long CustomerId { get; set; }

        public string DeliveryAddress { get; set; }

        public long TotalPriceCents { get; set; }

        public string TotalPriceCurrency { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        public string Status { get; set; }

        public OmsOrderItemMessage[] OrderItems { get; set; } = Array.Empty<OmsOrderItemMessage>();

        public override string RoutingKey => "order.created";
    }
}