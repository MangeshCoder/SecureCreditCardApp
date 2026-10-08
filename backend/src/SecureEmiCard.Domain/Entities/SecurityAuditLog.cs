using SecureEmiCard.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SecureEmiCard.Domain.Entities
{
    /// <summary>
    /// One row of the security audit trail. Maps to table dbo.SecurityAuditLogs.
    /// Append-only: there are no methods that change a row after it is created
    /// (and a database trigger rejects UPDATE/DELETE).
    /// </summary>
    public class SecurityAuditLog
    {
        // Required by EF Core
        private SecurityAuditLog() { }

        public static SecurityAuditLog Create(
        string actionType, string endpoint, AuditOutcome outcome, bool signatureValid, string payloadHash,
        int? httpStatus = null, int? userId = null, string? partnerId = null, string? clientIp = null,
        string? correlationId = null, string? detail = null) => new()
        {
            ActionType = Truncate(actionType, 50)!,
            Endpoint = Truncate(endpoint, 250)!,
            Outcome = outcome,
            SignatureValid = signatureValid,
            PayloadHash = Truncate(payloadHash, 256)!,
            HttpStatus = httpStatus,
            UserId = userId,
            PartnerId = Truncate(partnerId, 50),
            ClientIp = Truncate(clientIp, 45),
            CorrelationId = Truncate(correlationId, 64),
            Detail = Truncate(detail, 250),
            Timestamp = DateTime.UtcNow
        };

        public int AuditId { get; private set; }
        public string Endpoint { get; private set; } = string.Empty;
        public string ActionType { get; private set; } = string.Empty;
        /// <summary>True when the caller's credential was cryptographically verified (partner HMAC or user JWT).</summary>
        public bool SignatureValid { get; private set; }
        public string PayloadHash { get; private set; } = string.Empty;
        public DateTime Timestamp { get; private set; }
        public AuditOutcome Outcome { get; private set; }
        public int? HttpStatus { get; private set; }
        public int? UserId { get; private set; }
        public string? PartnerId { get; private set; }
        public string? ClientIp { get; private set; }
        public string? CorrelationId { get; private set; }
        public string? Detail { get; private set; }

        // Values are cut to the column size instead of failing the INSERT: losing an audit row
        // because a header was too long would be worse than storing a shortened value.
        private static string? Truncate(string? value, int max) =>
            value is null || value.Length <= max ? value : value[..max];
    }
}
