using SecureEmiCard.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SecureEmiCard.Application.Abstractions.Auditing
{
    /// <summary>Everything we record about one security-relevant request.</summary>
    public record AuditEntry(
        string ActionType,
        string Endpoint,
        AuditOutcome Outcome,
        bool SignatureValid,
        string PayloadHash,
        int? HttpStatus = null,
        int? UserId = null,
        string? PartnerId = null,
        string? ClientIp = null,
        string? CorrelationId = null,
        string? Detail = null);

    /// <summary>
    /// Writes audit rows. Implementations must save INDEPENDENTLY of the request's own database
    /// transaction: a failed login or a rejected signature has nothing to commit, but must still be recorded.
    /// </summary>
    public interface IAuditLogWriter
    {
        Task WriteAsync(AuditEntry entry, CancellationToken ct = default);
    }
}
