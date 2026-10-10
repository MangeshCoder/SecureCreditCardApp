using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Infrastructure.Persistence.Configurations;

/// <summary>
/// dbo.CardControls: one row per card, sharing the card's primary key (a 1:1 relationship).
/// The relationship itself is configured on CreditCard (HasOne(Controls)).
/// </summary>
public class CardControlConfiguration : IEntityTypeConfiguration<CardControl>
{
    public void Configure(EntityTypeBuilder<CardControl> b)
    {
        b.ToTable("CardControls");
        b.HasKey(x => x.CardId);
        b.Property(x => x.CardId).ValueGeneratedNever(); // = CreditCards.CardId

        b.Property(x => x.PosDailyLimit).HasPrecision(18, 2);
        b.Property(x => x.OnlineDailyLimit).HasPrecision(18, 2);
        b.Property(x => x.ContactlessDailyLimit).HasPrecision(18, 2);
        b.Property(x => x.AtmDailyLimit).HasPrecision(18, 2);
        b.Property(x => x.InternationalDailyLimit).HasPrecision(18, 2);
        b.Property(x => x.UpdatedAt).HasColumnType("datetime2");
    }
}
