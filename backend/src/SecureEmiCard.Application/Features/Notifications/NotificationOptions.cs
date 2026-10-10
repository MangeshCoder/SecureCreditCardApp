namespace SecureEmiCard.Application.Features.Notifications;

/// <summary>Alert delivery (Module 7). Optional "Notifications" config section; the values below are the defaults.</summary>
public class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>How often the dispatcher looks for alerts to send by SMS / e-mail.</summary>
    public int DispatchIntervalSeconds { get; set; } = 5;

    /// <summary>After this many failed attempts an alert is marked Failed (it stays in the in-app inbox).</summary>
    public int MaxDeliveryAttempts { get; set; } = 5;
}
