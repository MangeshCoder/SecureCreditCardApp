using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SecureEmiCard.Application.Abstractions.Persistence
{
    public record AuditLogFilter(string? ActionType, AuditOutcome? Outcome, string? PartnerId, int? UserId, DateTime? FromUtc);

    public interface IAuditLogRepository
    {
        /// <summary>Newest first.</summary>
        Task<(IReadOnlyList<SecurityAuditLog> Items, int TotalCount)> GetPagedAsync(
            AuditLogFilter filter, int page, int pageSize, CancellationToken ct = default);
    }
}
