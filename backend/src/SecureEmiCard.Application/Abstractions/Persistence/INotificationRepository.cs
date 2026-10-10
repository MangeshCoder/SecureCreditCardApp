using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Application.Abstractions.Persistence;

public interface INotificationRepository
{
    Task AddAsync(Notification notification, CancellationToken ct = default);
    Task<Notification?> GetByIdAsync(int notificationId, CancellationToken ct = default);

    /// <summary>Newest first.</summary>
    Task<(IReadOnlyList<Notification> Items, int TotalCount)> GetPagedAsync(
        int cardholderId, bool unreadOnly, int page, int pageSize, CancellationToken ct = default);

    Task<int> CountUnreadAsync(int cardholderId, CancellationToken ct = default);
    Task<IReadOnlyList<Notification>> GetUnreadAsync(int cardholderId, CancellationToken ct = default);
}
