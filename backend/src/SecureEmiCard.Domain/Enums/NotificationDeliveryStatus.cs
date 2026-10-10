namespace SecureEmiCard.Domain.Enums;

/// <summary>SMS / e-mail delivery state of a notification. Stored as NVARCHAR in Notifications.DeliveryStatus.</summary>
public enum NotificationDeliveryStatus
{
    /// <summary>Saved with the business change; the dispatcher has not sent it yet.</summary>
    Pending,
    Sent,
    /// <summary>Gave up after the maximum number of attempts.</summary>
    Failed
}
