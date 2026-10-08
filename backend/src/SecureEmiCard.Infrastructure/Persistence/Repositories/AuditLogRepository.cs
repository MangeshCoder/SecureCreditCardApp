using Microsoft.EntityFrameworkCore;
using SecureEmiCard.Application.Abstractions.Persistence;
using SecureEmiCard.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SecureEmiCard.Infrastructure.Persistence.Repositories
{
    public class AuditLogRepository : IAuditLogRepository
    {
        private readonly AppDbContext _db;

        public AuditLogRepository(AppDbContext db) => _db = db;

        public async Task<(IReadOnlyList<SecurityAuditLog> Items, int TotalCount)> GetPagedAsync(
            AuditLogFilter filter, int page, int pageSize, CancellationToken ct = default)
        {
            var query = _db.SecurityAuditLogs.AsNoTracking();
            if (filter.ActionType is not null) query = query.Where(a => a.ActionType == filter.ActionType);
            if (filter.Outcome is not null) query = query.Where(a => a.Outcome == filter.Outcome);
            if (filter.PartnerId is not null) query = query.Where(a => a.PartnerId == filter.PartnerId);
            if (filter.UserId is not null) query = query.Where(a => a.UserId == filter.UserId);
            if (filter.FromUtc is not null) query = query.Where(a => a.Timestamp >= filter.FromUtc);

            var total = await query.CountAsync(ct);
            var items = await query.OrderByDescending(a => a.Timestamp)
                                   .ThenByDescending(a => a.AuditId)
                                   .Skip((page - 1) * pageSize)
                                   .Take(pageSize)
                                   .ToListAsync(ct);
            return (items, total);
        }
    }
}
