using SecureEmiCard.Application.Abstractions.Persistence;
using SecureEmiCard.Application.Abstractions.Security;
using SecureEmiCard.Application.Common.Exceptions;
using SecureEmiCard.Application.Common.Models;
using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Application.Features.Notifications;

public record NotificationDto(int NotificationId, int? CardId, string Category, string Title, string Message,
                              DateTime CreatedAt, bool IsRead, string DeliveryStatus);

public record UnreadCountDto(int Count);

public interface INotificationService
{
    Task<PagedResult<NotificationDto>> GetMineAsync(bool unreadOnly, int page, int pageSize, CancellationToken ct = default);
    Task<UnreadCountDto> GetUnreadCountAsync(CancellationToken ct = default);
    Task MarkReadAsync(int notificationId, CancellationToken ct = default);
    Task<UnreadCountDto> MarkAllReadAsync(CancellationToken ct = default);
}

/// <summary>The in-app inbox: every user sees only their own alerts.</summary>
public class NotificationService : INotificationService
{
    private readonly INotificationRepository _notifications;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;

    public NotificationService(INotificationRepository notifications, IUnitOfWork unitOfWork, ICurrentUser currentUser)
    {
        _notifications = notifications;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<NotificationDto>> GetMineAsync(bool unreadOnly, int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = PagedResult<NotificationDto>.Normalize(page, pageSize);
        var (items, total) = await _notifications.GetPagedAsync(_currentUser.UserId, unreadOnly, page, pageSize, ct);
        return new PagedResult<NotificationDto>(items.Select(ToDto).ToList(), page, pageSize, total);
    }

    public async Task<UnreadCountDto> GetUnreadCountAsync(CancellationToken ct = default) =>
        new(await _notifications.CountUnreadAsync(_currentUser.UserId, ct));

    public async Task MarkReadAsync(int notificationId, CancellationToken ct = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, ct);
        if (notification is null || notification.CardholderId != _currentUser.UserId)
            throw new NotFoundException($"Notification {notificationId} was not found.");
        notification.MarkRead(DateTime.UtcNow);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<UnreadCountDto> MarkAllReadAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        foreach (var notification in await _notifications.GetUnreadAsync(_currentUser.UserId, ct))
            notification.MarkRead(now);
        await _unitOfWork.SaveChangesAsync(ct);
        return new UnreadCountDto(0);
    }

    private static NotificationDto ToDto(Notification n) =>
        new(n.NotificationId, n.CardId, n.Category.ToString(), n.Title, n.Message, n.CreatedAt, n.IsRead,
            n.DeliveryStatus.ToString());
}
