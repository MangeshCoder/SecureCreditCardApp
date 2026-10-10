using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Infrastructure.Persistence.Configurations;

public class CreditCardConfiguration : IEntityTypeConfiguration<CreditCard>
{
    public void Configure(EntityTypeBuilder<CreditCard> b)
    {
        b.ToTable("CreditCards");
        b.HasKey(x => x.CardId);
        b.Property(x => x.CardId).ValueGeneratedOnAdd();

        b.Property(x => x.CardNumberEncrypted).HasMaxLength(512).IsRequired();
        b.Property(x => x.CardNumberHash).HasMaxLength(64);
        b.HasIndex(x => x.CardNumberHash)
         .IsUnique()
         .HasFilter("[CardNumberHash] IS NOT NULL")
         .HasDatabaseName("UX_CreditCards_CardNumberHash");
        b.Property(x => x.MaskedCardNumber).HasMaxLength(20).IsRequired();
        b.Property(x => x.CvvHash).HasMaxLength(256).IsRequired();
        b.Property(x => x.PinHash).HasMaxLength(256).IsRequired();
        b.Property(x => x.CreditLimit).HasPrecision(18, 2);
        // Optimistic concurrency: EF adds "WHERE AvailableBalance = <value we read>" to every UPDATE.
        // If another request changed the balance in between, 0 rows are updated and EF throws,
        // so two simultaneous swipes can never both spend the same money.
        b.Property(x => x.AvailableBalance).HasPrecision(18, 2).IsConcurrencyToken();
        b.Property(x => x.FailedPinAttempts);
        b.Property(x => x.CardStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.ExpiryDate).HasColumnType("date");
        b.Property(x => x.CreatedAt).HasColumnType("datetime2");
        b.Property(x => x.LockedAt).HasColumnType("datetime2");
        // Module 8: a concurrency token too, so two billing runs for the same card (scheduler + the bank's button)
        // can't both close the same cycle - the second one fails and starts again.
        b.Property(x => x.LastStatementDate).HasColumnType("datetime2").IsConcurrencyToken();

        // Module 6: the card's controls live in their own table, keyed by the same CardId.
        b.HasOne(x => x.Controls)
         .WithOne()
         .HasForeignKey<CardControl>(x => x.CardId)
         .HasConstraintName("FK_CardControls_CreditCards")
         .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => x.CardholderId).HasDatabaseName("IX_CreditCards_CardholderId");

        b.Ignore(x => x.OutstandingAmount);
        b.Ignore(x => x.IsExpired);
        b.Ignore(x => x.IsActive);
        b.Ignore(x => x.RemainingPinAttempts);
    }
}
