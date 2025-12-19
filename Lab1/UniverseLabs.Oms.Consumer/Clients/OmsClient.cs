using System.Net.Http;
using System.Text;
using UniverseLabs.Oms.Consumer.Dto;

namespace UniverseLabs.Oms.Consumer.Clients;

public class OmsClient(HttpClient client)
{
    public async Task LogOrder(V1AuditLogOrderRequest request, CancellationToken token)
    {
        var msg = await client.PostAsync("api/v1/audit/log-order", new StringContent(request.ToJson(), Encoding.UTF8, "application/json"), token);
        msg.EnsureSuccessStatusCode();
    }
}