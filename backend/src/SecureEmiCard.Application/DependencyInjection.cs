using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SecureEmiCard.Application.Features.Auth;
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
        services.AddSingleton<ICardNumberGenerator, CardNumberGenerator>();

        return services;
    }
}
