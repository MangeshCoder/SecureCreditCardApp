using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureEmiCard.Application.Common.Models;
using SecureEmiCard.Application.Features.Notifications;

namespace SecureEmiCard.Api.Controllers;

/// <summary>Module 7 - the in-app inbox of alerts. Every user sees only their own.</summary>
[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notifications;

    public NotificationsController(INotificationService notifications) => _notifications = notifications;

    /// <summary>Newest first.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<NotificationDto>>> Get(
        [FromQuery] bool unreadOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await _notifications.GetMineAsync(unreadOnly, page, pageSize, ct));

    /// <summary>For the badge on the bell icon (polled by the Angular app).</summary>
    [HttpGet("unread-count")]
    public async Task<ActionResult<UnreadCountDto>> GetUnreadCount(CancellationToken ct)
        => Ok(await _notifications.GetUnreadCountAsync(ct));

    [HttpPost("{notificationId:int}/read")]
    public async Task<IActionResult> MarkRead(int notificationId, CancellationToken ct)
    {
        await _notifications.MarkReadAsync(notificationId, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<ActionResult<UnreadCountDto>> MarkAllRead(CancellationToken ct)
        => Ok(await _notifications.MarkAllReadAsync(ct));
}
