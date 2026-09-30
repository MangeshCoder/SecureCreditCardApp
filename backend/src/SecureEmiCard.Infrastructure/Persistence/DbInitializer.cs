using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecureEmiCard.Application.Abstractions.Security;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;

namespace SecureEmiCard.Infrastructure.Persistence;

/// <summary>
/// Runs at start-up: creates the in-memory database (demo mode) and seeds the first Admin
/// account from configuration ("SeedAdmin" section) if it does not exist yet.
/// For SQL Server the schema is created by the scripts in /database, not by EF.
/// </summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DbInitializer));

        if (db.Database.IsInMemory())
            await db.Database.EnsureCreatedAsync(ct);

        await BackfillCardNumberHashesAsync(scope.ServiceProvider, db, logger, ct);

        var email = config["SeedAdmin:Email"];
        var password = config["SeedAdmin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return;

        var normalized = email.Trim().ToLowerInvariant();
        if (await db.Cardholders.AnyAsync(c => c.Email == normalized, ct))
            return;

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        db.Cardholders.Add(new Cardholder("System", "Admin", normalized, "+10000000000",
                                          hasher.Hash(password), UserRole.Admin));
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded admin account {Email}", normalized);
    }

    /// <summary>
    /// Module 2 added CreditCards.CardNumberHash. Cards issued in Module 1 have NULL there;
    /// decrypt their number once and store the blind index so they can be swiped.
    /// </summary>
    private static async Task BackfillCardNumberHashesAsync(IServiceProvider sp, AppDbContext db, ILogger logger, CancellationToken ct)
    {
        var cards = await db.CreditCards.Where(c => c.CardNumberHash == null).ToListAsync(ct);
        if (cards.Count == 0) return;

        var encryption = sp.GetRequiredService<ICardEncryptionService>();
        var lookup = sp.GetRequiredService<ICardLookupHasher>();
        foreach (var card in cards)
            card.SetCardNumberHash(lookup.Compute(encryption.Decrypt(card.CardNumberEncrypted)));

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Back-filled the card number lookup hash for {Count} card(s)", cards.Count);
    }
}
