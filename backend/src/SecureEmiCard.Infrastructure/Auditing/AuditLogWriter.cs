using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecureEmiCard.Application.Abstractions.Auditing;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SecureEmiCard.Infrastructure.Auditing
{
    /// <summary>
    /// Saves audit rows through its OWN DbContext (a new DI scope), never through the request's DbContext:
    ///  - a rejected or failed request has nothing to commit, but its audit row must still be saved;
    ///  - the request's DbContext may hold half-finished changes that must NOT be saved by accident;
    ///  - ChangeTracker.Clear() during a concurrency retry must not drop audit rows.
    ///
    /// Failure policy: if the audit write itself fails (e.g. database down) the error is logged and the
    /// request continues ("fail open"). A stricter bank could choose "fail closed" and reject the operation.
    /// </summary>
    public class AuditLogWriter : IAuditLogWriter
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AuditLogWriter> _logger;

        public AuditLogWriter(IServiceScopeFactory scopeFactory, ILogger<AuditLogWriter> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task WriteAsync(AuditEntry e, CancellationToken ct = default)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.SecurityAuditLogs.Add(SecurityAuditLog.Create(
                    e.ActionType, e.Endpoint, e.Outcome, e.SignatureValid, e.PayloadHash, e.HttpStatus,
                    e.UserId, e.PartnerId, e.ClientIp, e.CorrelationId, e.Detail));
                // CancellationToken.None: the audit row is saved even if the client disconnects mid-request.
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AUDIT WRITE FAILED for {ActionType} {Outcome} on {Endpoint} (correlation {CorrelationId})",
                    e.ActionType, e.Outcome, e.Endpoint, e.CorrelationId);
            }
        }
    }
}
