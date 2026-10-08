using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SecureEmiCard.Domain.Enums
{
    /// <summary>Stored as NVARCHAR in SecurityAuditLogs.Outcome.</summary>
    public enum AuditOutcome
    {
        /// <summary>The operation was carried out.</summary>
        Success,
        /// <summary>A security check refused it: bad signature, replay, not authenticated/authorized, rate limit.</summary>
        Rejected,
        /// <summary>It was allowed but failed: validation, business rule or server error.</summary>
        Failed
    }
}
