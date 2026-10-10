using SecureEmiCard.Domain.Common;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.Domain.Entities;

/// <summary>
/// An alert for a cardholder (Module 7). Maps to dbo.Notifications.
/// It is the in-app inbox AND the "outbox" for SMS / e-mail: it is saved in the same database transaction
/// as the change it reports, and a background dispatcher delivers it afterwards.
/// Never put secrets (full card number, CVV, PIN, OTP codes) in a notification.
/// </summary>
public class Notification
{
    public const int MaxTitleLength = 100;
    public const int MaxMessageLength = 500;

    // Required by EF Core
    private Notification() { }

    /// <summary>Alert about a card. Uses the navigation, so EF Core fills in CardId even for a card that is
    /// being inserted in the same SaveChanges (e.g. "Your new card is ready").</summary>
    public static Notification ForCard(CreditCard card, NotificationCategory category, string title, string message)
    {
        var notification = ForCardholder(card.CardholderId, category, title, message);
        notification.Card = card;
        return notification;
    }

    /// <summary>Alert about the account (e.g. a new sign-in).</summary>
    public static Notification ForCardholder(int cardholderId, NotificationCategory category, string title, string message)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new DomainException("Notification title is required.");
        if (string.IsNullOrWhiteSpace(message)) throw new DomainException("Notification message is required.");

        return new Notification
        {
            CardholderId = cardholderId,
            Category = category,
            Title = Cut(title.Trim(), MaxTitleLength),
            Message = Cut(message.Trim(), MaxMessageLength),
            CreatedAt = DateTime.UtcNow,
            DeliveryStatus = NotificationDeliveryStatus.Pending
        };
    }

    public int NotificationId { get; private set; }
    public int CardholderId { get; private set; }
    public int? CardId { get; private set; }
    public NotificationCategory Category { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }
    public DateTime? ReadAt { get; private set; }
    public NotificationDeliveryStatus DeliveryStatus { get; private set; }
    public int DeliveryAttempts { get; private set; }
    public DateTime? SentAt { get; private set; }

    public Cardholder? Cardholder { get; private set; }
    public CreditCard? Card { get; private set; }

    public bool IsRead => ReadAt is not null;

    public void MarkRead(DateTime nowUtc) => ReadAt ??= nowUtc;

    public void MarkSent(DateTime nowUtc)
    {
        DeliveryAttempts++;
        DeliveryStatus = NotificationDeliveryStatus.Sent;
        SentAt = nowUtc;
    }

    /// <summary>The SMS / e-mail provider failed; retried later until <paramref name="maxAttempts"/>.</summary>
    public void RecordDeliveryFailure(int maxAttempts)
    {
        DeliveryAttempts++;
        if (DeliveryAttempts >= maxAttempts) DeliveryStatus = NotificationDeliveryStatus.Failed;
    }

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
}
