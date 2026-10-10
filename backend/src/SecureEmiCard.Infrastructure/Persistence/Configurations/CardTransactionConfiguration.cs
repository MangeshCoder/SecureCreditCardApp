using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Infrastructure.Persistence.Configurations;

public class CardTransactionConfiguration : IEntityTypeConfiguration<CardTransaction>
{
    public void Configure(EntityTypeBuilder<CardTransaction> b)
    {
        b.ToTable("Transactions");
        b.HasKey(x => x.TransactionId);
        b.Property(x => x.TransactionId).ValueGeneratedOnAdd();

        b.Property(x => x.MerchantName).HasMaxLength(CardTransaction.MaxMerchantNameLength).IsRequired();
        b.Property(x => x.MerchantCategoryCode).HasMaxLength(10).IsRequired();
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.TransactionType).HasConversion<string>().HasMaxLength(20).IsRequired();
        // Concurrency tokens (Module 4): a refund (Completed -> Refunded) and an EMI conversion
        // (IsEmiConverted 0 -> 1) of the same purchase at the same moment cannot both succeed.
        b.Property(x => x.TransactionStatus).HasConversion<string>().HasMaxLength(20).IsRequired().IsConcurrencyToken();
        b.Property(x => x.IsEmiConverted).IsConcurrencyToken();
        b.Property(x => x.TransactionDate).HasColumnType("datetime2");
        b.Property(x => x.DigitalSignature).HasMaxLength(512);
        b.Property(x => x.DeclineReason).HasMaxLength(100);
        // Module 6
        b.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.MerchantCountry).HasMaxLength(2).IsFixedLength();
        b.Ignore(x => x.IsCashWithdrawal);

        b.HasOne(x => x.Card)
         .WithMany()
         .HasForeignKey(x => x.CardId)
         .HasConstraintName("FK_Transactions_CreditCards")
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.CardId, x.TransactionDate }).HasDatabaseName("IX_Transactions_CardId_Date");
    }
}
