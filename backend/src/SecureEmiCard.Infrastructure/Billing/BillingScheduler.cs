using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureEmiCard.Application.Features.Billing;

namespace SecureEmiCard.Infrastructure.Billing;

/// <summary>
/// Module 8: the bank's nightly batch, every <see cref="BillingOptions.SchedulerInterval"/>:
///   1. statements whose due date passed → late fee / interest,
///   2. "payment due" reminders,
///   3. cards whose billing cycle ended → new statement.
/// Each step runs in its own DI scope (own DbContext), so one failing card is logged and skipped.
/// </summary>
public class BillingScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly BillingOptions _options;
    private readonly ILogger<BillingScheduler> _logger;

    public BillingScheduler(IServiceScopeFactory scopes, IOptions<BillingOptions> options, ILogger<BillingScheduler> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.SchedulerInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var result = await RunOnceAsync(stoppingToken);
                if (result != new BillingRunResult(0, 0, 0, 0))
                    _logger.LogInformation("Billing run: {Result}", result);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Billing run failed; will retry");
            }
        }
    }

    /// <summary>One full run. Public so tests can run it without waiting.</summary>
    public async Task<BillingRunResult> RunOnceAsync(CancellationToken ct = default)
    {
        int assessed = 0, reminders, generated = 0, failures = 0;

        foreach (var statementId in await WithRunnerAsync(r => r.GetStatementsToAssessAsync(ct)))
        {
            if (await TryAsync(r => r.AssessStatementAsync(statementId, ct), "assess statement", statementId)) assessed++;
            else failures++;
        }

        reminders = await WithRunnerAsync(r => r.SendPaymentRemindersAsync(ct));

        foreach (var cardId in await WithRunnerAsync(r => r.GetCardsDueForStatementAsync(ct)))
        {
            if (await TryAsync(r => r.GenerateScheduledStatementAsync(cardId, ct), "generate statement for card", cardId)) generated++;
            else failures++;
        }

        return new BillingRunResult(generated, assessed, reminders, failures);
    }

    private async Task<T> WithRunnerAsync<T>(Func<IBillingCycleRunner, Task<T>> step)
    {
        using var scope = _scopes.CreateScope();
        return await step(scope.ServiceProvider.GetRequiredService<IBillingCycleRunner>());
    }

    private async Task<bool> TryAsync(Func<IBillingCycleRunner, Task> step, string what, int id)
    {
        try
        {
            await WithRunnerAsync(async r => { await step(r); return true; });
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Billing: could not {What} {Id}", what, id);
            return false;
        }
    }
}
