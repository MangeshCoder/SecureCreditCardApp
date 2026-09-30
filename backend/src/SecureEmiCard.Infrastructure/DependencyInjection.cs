using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecureEmiCard.Application.Abstractions.Persistence;
using SecureEmiCard.Application.Abstractions.Security;
using SecureEmiCard.Infrastructure.Persistence;
using SecureEmiCard.Infrastructure.Persistence.Repositories;
using SecureEmiCard.Infrastructure.Security;

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
                options.UseInMemoryDatabase("SecureEmiCardDb");
            else
                options.UseSqlServer(configuration.GetConnectionString("SecureEmiCardDb"),
                    sql => sql.EnableRetryOnFailure());
        });
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<ICardholderRepository, CardholderRepository>();
        services.AddScoped<ICreditCardRepository, CreditCardRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();

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
