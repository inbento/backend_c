using Lab1.BBL.Models;
using Lab1.BBL.Services;
using Lab1.Validators;
using Lab1.DAL.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Lab1.Controllers
{


    [ApiController]
    [Route("api/v1/audit-log-order")]
    public class AuditLogOrderController : ControllerBase
    {
        private readonly AuditLogOrderService _auditLogOrderService;
        private readonly V1AuditLogOrderRequestValidator _validator;
        private readonly ILogger<AuditLogOrderController> _logger;

        public AuditLogOrderController(AuditLogOrderService auditLogOrderService, V1AuditLogOrderRequestValidator validator, ILogger<AuditLogOrderController> logger)
        {
            _auditLogOrderService = auditLogOrderService;
            _validator = validator;
            _logger = logger;
        }

        [HttpPost]
        public async Task<ActionResult<V1AuditLogOrderResponse>> CreateAuditLogs(
            [FromBody] V1AuditLogOrderRequest request,
            CancellationToken token)
        {
            _logger.LogInformation("Received CreateAuditLogs request. Orders count: {OrdersCount}", request.Orders?.Length ?? 0);

            if (request.Orders == null || request.Orders.Length == 0)
            {
                _logger.LogWarning("Received CreateAuditLogs request with empty or null orders array.");
            }

            var validationResult = await _validator.ValidateAsync(request, token);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("CreateAuditLogs request failed validation. Errors: {Errors}", string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage)));
                return BadRequest(validationResult.Errors);
            }

            var result = await _auditLogOrderService.CreateAuditLogs(request, token);
            _logger.LogInformation("CreateAuditLogs request processed successfully.");
            return Ok(result);
        }

        [HttpGet]
        public async Task<ActionResult<V1AuditLogOrderResponse>> GetAuditLogs(
            [FromQuery] QueryAuditLogOrderModel model,
            CancellationToken token)
        {
            var result = await _auditLogOrderService.GetAuditLogs(model, token);
            return Ok(result);
        }
    }
}
