using SecureEmiCard.Application.Abstractions.Persistence;
using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Application.Features.Notifications;

public interface INotifier
{
    /// <summary>
    /// Adds an alert for a cardholder. It is NOT sent here: it is saved by the caller's SaveChanges, in the
    /// same database transaction as the change it reports ("transactional outbox"), and the dispatcher
    /// sends it by SMS / e-mail afterwards. So there is never an alert for a change that was rolled back,
    /// and a slow or failing SMS provider never breaks a payment.
    /// </summary>
    Task AddAsync(CreditCard card, Alert alert, CancellationToken ct = default);

    /// <summary>Alert about the account rather than a card (e.g. a new sign-in).</summary>
    Task AddAsync(int cardholderId, Alert alert, CancellationToken ct = default);
}

public class Notifier : INotifier
{
    private readonly INotificationRepository _notifications;

    public Notifier(INotificationRepository notifications) => _notifications = notifications;

    public Task AddAsync(CreditCard card, Alert alert, CancellationToken ct = default) =>
        _notifications.AddAsync(Notification.ForCard(card, alert.Category, alert.Title, alert.Message), ct);

    public Task AddAsync(int cardholderId, Alert alert, CancellationToken ct = default) =>
        _notifications.AddAsync(Notification.ForCardholder(cardholderId, alert.Category, alert.Title, alert.Message), ct);
}
