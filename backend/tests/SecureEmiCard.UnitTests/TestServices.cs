using Microsoft.Extensions.Options;
using SecureEmiCard.Application.Abstractions.Messaging;
using SecureEmiCard.Application.Abstractions.Security;
using SecureEmiCard.Application.Common.Exceptions;
using SecureEmiCard.Application.Features.Billing;
using SecureEmiCard.Application.Features.CardControls;
using SecureEmiCard.Application.Features.Cards;
using SecureEmiCard.Application.Features.Cashback;
using SecureEmiCard.Application.Features.Emi;
using SecureEmiCard.Application.Features.Notifications;
using SecureEmiCard.Application.Features.Otp;
using SecureEmiCard.Application.Features.Transactions;
using SecureEmiCard.Infrastructure.Billing;
using SecureEmiCard.Infrastructure.Persistence;
using SecureEmiCard.Infrastructure.Persistence.Repositories;
using SecureEmiCard.Infrastructure.Security;

namespace SecureEmiCard.UnitTests;

/// <summary>
/// Builds the application services the way the DI container does, over ONE EF Core in-memory database,
/// with the real crypto and a fake phone (<see cref="TestOtp"/>). One place to wire new dependencies.
/// </summary>
internal sealed class TestServices
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _user;

    public TestServices(AppDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public TestOtp Otp { get; } = new();
    public OtpOptions OtpOptions { get; } = new();
    public PepperedSecretHasher Hasher { get; } = new(TestKeys.Encryption(), iterations: 1_000);
    public HmacCardLookupHasher Lookup { get; } = new(TestKeys.Encryption());
    public CardControlRules ControlRules { get; } = new(Options.Create(new CardControlOptions()));
    public BillingOptions BillingOptions { get; } = new();
    public BillingCalculator Billing => new(Options.Create(BillingOptions));
    /// <summary>Billing's clock - move it forward to reach a due date without waiting.</summary>
    public ShiftedClock Clock { get; } = new();

    public StepUpAuthenticator StepUp() =>
        new(new OtpChallengeRepository(_db), new CardholderRepository(_db), _db, Hasher, Otp, Otp, Options.Create(OtpOptions));

    public Notifier Notifier() => new(new NotificationRepository(_db));

    public CardService Cards() =>
        new(new CreditCardRepository(_db), new CardholderRepository(_db), _db,
            new AesGcmCardEncryptionService(TestKeys.Encryption()), Lookup, Hasher, new CardNumberGenerator(), _user,
            StepUp(), Notifier(),
            new IssueCardRequestValidator(), new UpdateCreditLimitRequestValidator(),
            new ChangePinRequestValidator(), new RevealCardNumberRequestValidator());

    public TransactionService Transactions(ICashbackEngine? cashback = null) =>
        new(new CreditCardRepository(_db), new TransactionRepository(_db), new CashbackRepository(_db),
            cashback ?? new CashbackEngine(Options.Create(new CashbackOptions())), new EmiPlanRepository(_db), _db,
            Lookup, Hasher, _user, ControlRules, StepUp(), Notifier(), Billing,
            new SwipeRequestValidator(), new LoadRequestValidator());

    public CardControlService CardControls() =>
        new(new CreditCardRepository(_db), new TransactionRepository(_db), _db, _user, ControlRules,
            new UpdateCardControlsRequestValidator(), StepUp(), Notifier());

    public BillingService BillingService() =>
        new(new CreditCardRepository(_db), new CardStatementRepository(_db), new TransactionRepository(_db),
            new CashbackRepository(_db), new EmiPlanRepository(_db), _db, _user, Notifier(), Billing,
            new QuestPdfStatementRenderer(), ControlRules, Clock);

    public EmiService Emi() =>
        new(new EmiCalculator(Options.Create(new EmiOptions())), new EmiPlanRepository(_db), new TransactionRepository(_db),
            new CreditCardRepository(_db), _db, _user, Notifier(),
            new EmiPreviewRequestValidator(), new ConvertToEmiRequestValidator());
}

/// <summary>The real time plus an offset: "it is now 21 days later" for the billing code.</summary>
internal sealed class ShiftedClock : TimeProvider
{
    public TimeSpan Offset { get; set; }

    public void Advance(TimeSpan by) => Offset += by;

    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + Offset;
}

/// <summary>
/// The cardholder's phone in tests: captures SMS / e-mail, and types the one-time code back in like a user.
/// </summary>
internal sealed class TestOtp : IOtpProofAccessor, IMessageSender
{
    public List<(string To, string Text)> Sms { get; } = new();
    public List<(string To, string Subject, string Body)> Emails { get; } = new();

    /// <summary>What the "client" sends with the next request (the X-Otp-* headers in the real API).</summary>
    public OtpProof? Current { get; set; }

    public Task SendSmsAsync(string phoneNumber, string text, CancellationToken ct = default)
    {
        Sms.Add((phoneNumber, text));
        return Task.CompletedTask;
    }

    public Task SendEmailAsync(string email, string subject, string body, CancellationToken ct = default)
    {
        Emails.Add((email, subject, body));
        return Task.CompletedTask;
    }

    public IEnumerable<string> OtpMessages => Sms.Select(s => s.Text).Where(t => t.Contains(" is your Secure Credit EMI code "));

    /// <summary>The code from the last OTP SMS ("123456 is your Secure Credit EMI code ...").</summary>
    public string LastCode => OtpMessages.Last()[..6];

    /// <summary>The action MUST ask for a code; answer it with the code from the SMS and run it again.</summary>
    public async Task<T> ApproveAsync<T>(Func<Task<T>> action)
    {
        var required = await Assert.ThrowsAsync<OtpRequiredException>(action);
        return await RetryWithCodeAsync(required, action);
    }

    public Task ApproveAsync(Func<Task> action) => ApproveAsync(async () => { await action(); return true; });

    /// <summary>Like the Angular app: if the action asks for a code, enter it; otherwise just return the result.</summary>
    public async Task<T> CompleteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (OtpRequiredException required)
        {
            return await RetryWithCodeAsync(required, action);
        }
    }

    private async Task<T> RetryWithCodeAsync<T>(OtpRequiredException required, Func<Task<T>> action)
    {
        Current = new OtpProof(required.Challenge.ChallengeId, LastCode);
        try
        {
            return await action();
        }
        finally
        {
            Current = null;
        }
    }
}
