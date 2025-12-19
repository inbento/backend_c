using Microsoft.AspNetCore.Mvc;
using Models.Dto.V1.Requests;
using Models.Dto.V1.Responses;
using WebApi.BLL.Services;
using WebApi.DAL.Models;
using WebApi.Validators;

namespace WebApi.Controllers.V1;

[Route("api/v1/audit")]
public class AuditController(AuditLogOrderService auditLogOrderService, ValidatorFactory validatorFactory): ControllerBase
{
    [HttpPost("log-order")]
    public async Task<ActionResult<V1AuditLogOrderResponse>> LogOrder([FromBody] V1AuditLogOrderRequest request, CancellationToken token)
    {
        var validationResult = await validatorFactory.GetValidator<V1AuditLogOrderRequest>().ValidateAsync(request, token);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.ToDictionary());
        }

        var now = DateTimeOffset.UtcNow;
        var logs = request.Orders.Select(o => new V1AuditLogOrderDal
        {
            OrderId = o.OrderId,
            OrderItemId = o.OrderItemId,
            CustomerId = o.CustomerId,
            OrderStatus = o.OrderStatus,
            CreatedAt = now,
            UpdatedAt = now
        }).ToArray();

        await auditLogOrderService.LogOrders(logs, token);

        return Ok(new V1AuditLogOrderResponse { Status = "Logged" });
    }
}