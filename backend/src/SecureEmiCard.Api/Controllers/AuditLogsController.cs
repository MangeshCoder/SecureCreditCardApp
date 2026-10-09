using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SecureEmiCard.Application.Common.Models;
using SecureEmiCard.Application.Features.Audit;

namespace SecureEmiCard.Api.Controllers
{
    /// <summary>Security audit trail for the bank's security team. Read-only, Admin only.</summary>
    [ApiController]
    [Route("api/audit-logs")]
    [Authorize(Roles = "Admin")]
    public class AuditLogsController : ControllerBase
    {
        private readonly IAuditLogService _audit;

        public AuditLogsController(IAuditLogService audit) => _audit = audit;

        /// <summary>Newest first. All filters are optional; lastHours=24 shows the last day.</summary>
        [HttpGet]
        public async Task<ActionResult<PagedResult<AuditLogDto>>> Get(
            [FromQuery] string? actionType, [FromQuery] string? outcome, [FromQuery] string? partnerId,
            [FromQuery] int? userId, [FromQuery] int? lastHours,
            [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
            => Ok(await _audit.GetAsync(actionType, outcome, partnerId, userId, lastHours, page, pageSize, ct));

        [HttpGet("action-types")]
        public ActionResult<IReadOnlyList<string>> GetActionTypes() => Ok(_audit.GetActionTypes());
    }
}
