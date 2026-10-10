using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SecureEmiCard.Application.Features.Auth;
using SecureEmiCard.Application.Features.Cashback;
using SecureEmiCard.Application.Features.Emi;
using SecureEmiCard.Application.Features.Notifications;
using SecureEmiCard.Application.Features.Otp;
using SecureEmiCard.Application.Features.CardControls;
using SecureEmiCard.Application.Features.Cardholders;
using SecureEmiCard.Application.Features.Cards;
using SecureEmiCard.Application.Features.Transactions;
using SecureEmiCard.Application.Features.Audit;

namespace SecureEmiCard.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ICardholderService, CardholderService>();
        services.AddScoped<ICardService, CardService>();
        services.AddScoped<ITransactionService, TransactionService>();
        services.AddScoped<ICashbackService, CashbackService>();
        services.AddSingleton<ICashbackEngine, CashbackEngine>();
        services.AddScoped<IEmiService, EmiService>();
        services.AddSingleton<IEmiCalculator, EmiCalculator>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<ICardControlService, CardControlService>();
        services.AddSingleton<ICardControlRules, CardControlRules>();
        services.AddScoped<IStepUpAuthenticator, StepUpAuthenticator>(); // scoped: remembers verified codes per request
        services.AddScoped<INotifier, Notifier>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddSingleton<ICardNumberGenerator, CardNumberGenerator>();

        return services;
    }
}
