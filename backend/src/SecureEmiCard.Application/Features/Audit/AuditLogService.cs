using SecureEmiCard.Application.Abstractions.Persistence;
using SecureEmiCard.Application.Abstractions.Security;
using SecureEmiCard.Application.Common.Exceptions;
using SecureEmiCard.Application.Common.Models;
using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SecureEmiCard.Application.Features.Audit
{
    public record AuditLogDto(
        int AuditId,
        DateTime Timestamp,
        string ActionType,
        string Outcome,
        int? HttpStatus,
        string Endpoint,
        bool SignatureValid,
        int? UserId,
        string? PartnerId,
        string? ClientIp,
        string? CorrelationId,
        string? Detail,
        string PayloadHash);

    public interface IAuditLogService
    {
        Task<PagedResult<AuditLogDto>> GetAsync(string? actionType, string? outcome, string? partnerId, int? userId,
                                                int? lastHours, int page, int pageSize, CancellationToken ct = default);
        IReadOnlyList<string> GetActionTypes();
    }

    /// <summary>Read-only access to the audit trail for the bank's security team (Admin role).</summary>
    public class AuditLogService : IAuditLogService
    {
        private readonly IAuditLogRepository _logs;
        private readonly ICurrentUser _currentUser;

        public AuditLogService(IAuditLogRepository logs, ICurrentUser currentUser)
        {
            _logs = logs;
            _currentUser = currentUser;
        }

        public async Task<PagedResult<AuditLogDto>> GetAsync(string? actionType, string? outcome, string? partnerId, int? userId,
                                                             int? lastHours, int page, int pageSize, CancellationToken ct = default)
        {
            if (!_currentUser.IsAdmin) throw new ForbiddenException("This operation requires the Admin role.");

            AuditOutcome? parsedOutcome = null;
            if (!string.IsNullOrWhiteSpace(outcome))
            {
                if (!Enum.TryParse<AuditOutcome>(outcome, ignoreCase: true, out var o))
                    throw new DomainException("Outcome must be Success, Rejected or Failed.");
                parsedOutcome = o;
            }

            var filter = new AuditLogFilter(
                string.IsNullOrWhiteSpace(actionType) ? null : actionType.Trim(),
                parsedOutcome,
                string.IsNullOrWhiteSpace(partnerId) ? null : partnerId.Trim(),
                userId,
                lastHours is > 0 ? DateTime.UtcNow.AddHours(-lastHours.Value) : null);

            (page, pageSize) = PagedResult<AuditLogDto>.Normalize(page, pageSize);
            var (items, total) = await _logs.GetPagedAsync(filter, page, pageSize, ct);

            return new PagedResult<AuditLogDto>(items.Select(a => new AuditLogDto(
                a.AuditId, a.Timestamp, a.ActionType, a.Outcome.ToString(), a.HttpStatus, a.Endpoint, a.SignatureValid,
                a.UserId, a.PartnerId, a.ClientIp, a.CorrelationId, a.Detail, a.PayloadHash)).ToList(), page, pageSize, total);
        }

        public IReadOnlyList<string> GetActionTypes() => AuditActions.All;
    }
}
