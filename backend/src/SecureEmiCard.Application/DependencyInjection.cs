using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SecureEmiCard.Application.Features.Auth;
using SecureEmiCard.Application.Features.Cashback;
using SecureEmiCard.Application.Features.Emi;
using SecureEmiCard.Application.Features.Cardholders;
using SecureEmiCard.Application.Features.Cards;
using SecureEmiCard.Application.Features.Transactions;

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
        services.AddSingleton<ICardNumberGenerator, CardNumberGenerator>();

        return services;
    }
}
