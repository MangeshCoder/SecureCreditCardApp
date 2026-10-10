using Microsoft.EntityFrameworkCore;
using SecureEmiCard.Application.Abstractions.Persistence;
using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Infrastructure.Persistence.Repositories;

public class NotificationRepository : INotificationRepository
{
    private readonly AppDbContext _db;

    public NotificationRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(Notification notification, CancellationToken ct = default) =>
        await _db.Notifications.AddAsync(notification, ct);

    public Task<Notification?> GetByIdAsync(int notificationId, CancellationToken ct = default) =>
        _db.Notifications.FirstOrDefaultAsync(n => n.NotificationId == notificationId, ct);

    public async Task<(IReadOnlyList<Notification> Items, int TotalCount)> GetPagedAsync(
        int cardholderId, bool unreadOnly, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.Notifications.AsNoTracking().Where(n => n.CardholderId == cardholderId);
        if (unreadOnly) query = query.Where(n => n.ReadAt == null);

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.NotificationId)
                               .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }

    public Task<int> CountUnreadAsync(int cardholderId, CancellationToken ct = default) =>
        _db.Notifications.CountAsync(n => n.CardholderId == cardholderId && n.ReadAt == null, ct);

    public async Task<IReadOnlyList<Notification>> GetUnreadAsync(int cardholderId, CancellationToken ct = default) =>
        await _db.Notifications.Where(n => n.CardholderId == cardholderId && n.ReadAt == null).ToListAsync(ct);
}
