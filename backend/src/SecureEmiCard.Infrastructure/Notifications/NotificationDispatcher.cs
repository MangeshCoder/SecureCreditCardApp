using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureEmiCard.Application.Abstractions.Messaging;
using SecureEmiCard.Application.Features.Notifications;
using SecureEmiCard.Domain.Enums;
using SecureEmiCard.Infrastructure.Persistence;

namespace SecureEmiCard.Infrastructure.Notifications;

/// <summary>
/// The other half of the transactional outbox (Module 7): every few seconds it takes the alerts that were
/// saved with their business change and sends them by SMS and e-mail. If the provider is down, the alert
/// stays Pending and is retried - the payment that caused it was never affected.
/// (With several API servers, two dispatchers could pick the same row; production would claim rows first,
/// e.g. UPDATE ... OUTPUT with READPAST, or use a message queue.)
/// </summary>
public class NotificationDispatcher : BackgroundService
{
    private const int BatchSize = 50;

    private readonly IServiceScopeFactory _scopes;
    private readonly IMessageSender _sender;
    private readonly NotificationOptions _options;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(IServiceScopeFactory scopes, IMessageSender sender,
                                  IOptions<NotificationOptions> options, ILogger<NotificationDispatcher> logger)
    {
        _scopes = scopes;
        _sender = sender;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.DispatchIntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await DispatchPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Notification dispatch failed; will retry");
            }
        }
    }

    /// <summary>Sends one batch. Public so tests can run it without waiting for the timer.</summary>
    public async Task<int> DispatchPendingAsync(CancellationToken ct = default)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pending = await db.Notifications.Include(n => n.Cardholder)
                              .Where(n => n.DeliveryStatus == NotificationDeliveryStatus.Pending)
                              .OrderBy(n => n.NotificationId)
                              .Take(BatchSize)
                              .ToListAsync(ct);
        if (pending.Count == 0) return 0;

        foreach (var notification in pending)
        {
            try
            {
                var to = notification.Cardholder!;
                await _sender.SendSmsAsync(to.PhoneNumber, $"Secure Credit EMI: {notification.Message}", ct);
                await _sender.SendEmailAsync(to.Email, $"Secure Credit EMI - {notification.Title}", notification.Message, ct);
                notification.MarkSent(DateTime.UtcNow);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not deliver notification {Id}", notification.NotificationId);
                notification.RecordDeliveryFailure(_options.MaxDeliveryAttempts);
            }
        }

        await db.SaveChangesAsync(ct);
        return pending.Count;
    }
}
