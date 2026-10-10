using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecureEmiCard.Application.Abstractions.Billing;
using SecureEmiCard.Application.Abstractions.Messaging;
using SecureEmiCard.Application.Abstractions.Persistence;
using SecureEmiCard.Application.Abstractions.Security;
using SecureEmiCard.Application.Features.Billing;
using SecureEmiCard.Application.Features.CardControls;
using SecureEmiCard.Application.Features.Cashback;
using SecureEmiCard.Application.Features.Emi;
using SecureEmiCard.Application.Features.Notifications;
using SecureEmiCard.Application.Features.Otp;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Infrastructure.Persistence;
using SecureEmiCard.Infrastructure.Persistence.Repositories;
using SecureEmiCard.Infrastructure.Security;
using SecureEmiCard.Application.Abstractions.Auditing;
using SecureEmiCard.Infrastructure.Auditing;
using SecureEmiCard.Infrastructure.Billing;
using SecureEmiCard.Infrastructure.Notifications;

namespace SecureEmiCard.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // ---- Persistence -----------------------------------------------------------
        // "Database:UseInMemory": true lets you run the whole app without SQL Server (demo / learning).
        var useInMemory = configuration.GetValue<bool>("Database:UseInMemory");
        services.AddDbContext<AppDbContext>(options =>
        {
            if (useInMemory)
                options.UseInMemoryDatabase(configuration["Database:InMemoryName"] ?? "SecureEmiCardDb");
            else
                options.UseSqlServer(configuration.GetConnectionString("SecureEmiCardDb"),
                    sql => sql.EnableRetryOnFailure());
        });
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<ICardholderRepository, CardholderRepository>();
        services.AddScoped<ICreditCardRepository, CreditCardRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<ICashbackRepository, CashbackRepository>();
        services.AddScoped<IEmiPlanRepository, EmiPlanRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddSingleton<IAuditLogWriter, AuditLogWriter>();
        services.AddScoped<IOtpChallengeRepository, OtpChallengeRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<ICardStatementRepository, CardStatementRepository>();

        // ---- OTP + alerts (Module 7) ------------------------------------------------------------
        services.AddOptions<OtpOptions>()
            .Bind(configuration.GetSection(OtpOptions.SectionName))
            .Validate(o => o.ExpiryMinutes is >= 1 and <= 15, "Otp:ExpiryMinutes must be 1-15.")
            .Validate(o => o.MaxAttempts is >= 1 and <= 5, "Otp:MaxAttempts must be 1-5.")
            .Validate(o => o.MaxCodesPerWindow >= 1 && o.WindowMinutes >= 1, "Otp:MaxCodesPerWindow and WindowMinutes must be >= 1.")
            .ValidateOnStart();
        services.AddOptions<NotificationOptions>()
            .Bind(configuration.GetSection(NotificationOptions.SectionName))
            .Validate(o => o.DispatchIntervalSeconds is >= 1 and <= 3600, "Notifications:DispatchIntervalSeconds must be 1-3600.")
            .Validate(o => o.MaxDeliveryAttempts >= 1, "Notifications:MaxDeliveryAttempts must be >= 1.")
            .ValidateOnStart();
        // Development / tests: messages go to an in-memory outbox (Phone simulator). Production replaces
        // IMessageSender with a real SMS gateway + e-mail service; Program.cs enforces that.
        services.AddSingleton<DevMessageOutbox>();
        services.AddSingleton<IMessageSender, SimulatedMessageSender>();
        services.AddSingleton<NotificationDispatcher>();
        services.AddHostedService(sp => sp.GetRequiredService<NotificationDispatcher>());

        // ---- Cashback rules (Module 3) ----------------------------------------------------
        // Defaults live in CashbackOptions; the optional "Cashback" config section overrides them.
        services.AddOptions<CashbackOptions>()
            .Bind(configuration.GetSection(CashbackOptions.SectionName))
            .Validate(o => o.DefaultPercentage is >= 0 and <= 100, "Cashback:DefaultPercentage must be between 0 and 100.")
            .Validate(o => o.CategoryPercentages.Values.All(p => p is >= 0 and <= 100), "Cashback:CategoryPercentages must be between 0 and 100.")
            .Validate(o => o.CategoryPercentages.Keys.All(k => k.Length == 4 && k.All(char.IsAsciiDigit)), "Cashback:CategoryPercentages keys must be 4-digit MCCs.")
            .Validate(o => o.MinimumSpend >= 0 && o.MaxCashbackPerTransaction > 0, "Cashback:MinimumSpend must be >= 0 and MaxCashbackPerTransaction > 0.")
            .ValidateOnStart();

        // ---- EMI rules (Module 4) ------------------------------------------------------------
        services.AddOptions<EmiOptions>()
            .Bind(configuration.GetSection(EmiOptions.SectionName))
            .Validate(o => o.MinimumAmount >= 0, "Emi:MinimumAmount must be >= 0.")
            .Validate(o => o.ConversionWindowDays > 0, "Emi:ConversionWindowDays must be > 0.")
            .Validate(o => o.AnnualInterestRates.Count > 0 && o.AnnualInterestRates.Keys.All(t => EmiPlan.AllowedTenures.Contains(t)),
                      "Emi:AnnualInterestRates keys must be tenures from: 3, 6, 12, 24.")
            .Validate(o => o.AnnualInterestRates.Values.All(r => r is >= 0 and <= 60), "Emi:AnnualInterestRates must be between 0 and 60.")
            .ValidateOnStart();

        // ---- Card controls (Module 6) ----------------------------------------------------------
        services.AddOptions<CardControlOptions>()
            .Bind(configuration.GetSection(CardControlOptions.SectionName))
            .Validate(o => o.HomeCountryCode is { Length: 2 } && o.HomeCountryCode.All(char.IsAsciiLetterUpper),
                      "CardControls:HomeCountryCode must be a 2-letter upper-case ISO code, e.g. IN.")
            .Validate(o => o.ContactlessPerTransactionLimit > 0, "CardControls:ContactlessPerTransactionLimit must be > 0.")
            .Validate(o => o.BusinessDayUtcOffset >= TimeSpan.FromHours(-12) && o.BusinessDayUtcOffset <= TimeSpan.FromHours(14),
                      "CardControls:BusinessDayUtcOffset must be between -12:00 and +14:00.")
            .ValidateOnStart();

        // ---- Billing (Module 8) --------------------------------------------------------------
        services.AddOptions<BillingOptions>()
            .Bind(configuration.GetSection(BillingOptions.SectionName))
            .Validate(o => o.CycleLength > TimeSpan.Zero && o.PaymentDuePeriod > TimeSpan.Zero,
                      "Billing:CycleLength and PaymentDuePeriod must be positive.")
            .Validate(o => o.MinimumDuePercent is > 0 and <= 100 && o.MinimumDueFloor >= 0,
                      "Billing:MinimumDuePercent must be 0-100 and MinimumDueFloor >= 0.")
            .Validate(o => o.MonthlyInterestPercent is >= 0 and <= 10, "Billing:MonthlyInterestPercent must be 0-10.")
            .Validate(o => o.CashAdvanceFeePercent is >= 0 and <= 10 && o.CashAdvanceMinimumFee >= 0,
                      "Billing:CashAdvanceFeePercent must be 0-10 and CashAdvanceMinimumFee >= 0.")
            .Validate(o => o.GstPercent is >= 0 and <= 50, "Billing:GstPercent must be 0-50.")
            .Validate(o => o.LateFeeSlabs.Count == 0 || (o.LateFeeSlabs.Last().UpTo is null &&
                           o.LateFeeSlabs.SkipLast(1).All(s => s.UpTo > 0) &&
                           o.LateFeeSlabs.SkipLast(1).Select(s => s.UpTo!.Value).SequenceEqual(
                               o.LateFeeSlabs.SkipLast(1).Select(s => s.UpTo!.Value).OrderBy(v => v)) &&
                           o.LateFeeSlabs.All(s => s.Fee >= 0)),
                      "Billing:LateFeeSlabs must be ascending by UpTo, end with an open slab (UpTo null), fees >= 0.")
            .Validate(o => o.SchedulerInterval >= TimeSpan.FromSeconds(1) && o.ReminderBeforeDue >= TimeSpan.Zero,
                      "Billing:SchedulerInterval must be at least 1 second.")
            .ValidateOnStart();
        services.AddSingleton<IStatementPdfRenderer, QuestPdfStatementRenderer>();
        services.AddSingleton<BillingScheduler>();
        services.AddHostedService(sp => sp.GetRequiredService<BillingScheduler>());

        // ---- Security --------------------------------------------------------------
        services.AddOptions<EncryptionOptions>()
            .Bind(configuration.GetSection(EncryptionOptions.SectionName))
            .Validate(o => IsBase64OfLength(o.CardDataKey, 32, exact: true), "Encryption:CardDataKey must be a Base64 32-byte key.")
            .Validate(o => IsBase64OfLength(o.SecretPepper, 32, exact: false), "Encryption:SecretPepper must be a Base64 key of at least 32 bytes.")
            .ValidateOnStart();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => IsBase64OfLength(o.SigningKey, 64, exact: false), "Jwt:SigningKey must be a Base64 key of at least 64 bytes.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer) && !string.IsNullOrWhiteSpace(o.Audience), "Jwt:Issuer and Jwt:Audience are required.")
            .Validate(o => o.ExpiryMinutes is > 0 and <= 1440, "Jwt:ExpiryMinutes must be between 1 and 1440.")
            .ValidateOnStart();

        services.AddSingleton<ICardEncryptionService, AesGcmCardEncryptionService>();
        services.AddSingleton<ISecretHasher, PepperedSecretHasher>();
        services.AddSingleton<ICardLookupHasher, HmacCardLookupHasher>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddSingleton<IPayloadCryptoService, AesCbcPayloadCryptoService>();
        services.AddSingleton<ISignatureService, HmacSignatureService>();

        return services;
    }

    private static bool IsBase64OfLength(string? value, int length, bool exact)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var buffer = new byte[value.Length];
        if (!Convert.TryFromBase64String(value, buffer, out int written)) return false;
        return exact ? written == length : written >= length;
    }
}
