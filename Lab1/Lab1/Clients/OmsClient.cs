using Lab1.DAL.Models;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Lab1.Clients
{
    public class OmsClient(HttpClient client, ILogger<OmsClient> logger)
    {
        public async Task<V1AuditLogOrderResponse> LogOrder(V1AuditLogOrderRequest request, CancellationToken token)
        {
            var requestJson = JsonSerializer.Serialize(request);
            logger.LogInformation("Sending LogOrder request: {RequestJson}", requestJson);

            var response = await client.PostAsync(
                "api/v1/audit-log-order",
                new StringContent(requestJson, Encoding.UTF8, "application/json"),
                token
            );

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(token);
                logger.LogError("LogOrder request failed with status code {StatusCode}. Details: {ErrorContent}", (int)response.StatusCode, errorContent);
                throw new HttpRequestException($"HTTP error: {(int)response.StatusCode}. Details: {errorContent}");
            }

            var content = await response.Content.ReadAsStringAsync(token);
            logger.LogInformation("LogOrder request successful. Response: {ResponseContent}", content);
            return JsonSerializer.Deserialize<V1AuditLogOrderResponse>(content)!;
        }
    }
}
