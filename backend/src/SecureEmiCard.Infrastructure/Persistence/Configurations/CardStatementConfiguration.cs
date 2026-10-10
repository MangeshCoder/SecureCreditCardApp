using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Infrastructure.Persistence.Configurations;

public class CardStatementConfiguration : IEntityTypeConfiguration<CardStatement>
{
    public void Configure(EntityTypeBuilder<CardStatement> b)
    {
        b.ToTable("CardStatements");
        b.HasKey(x => x.StatementId);
        b.Property(x => x.StatementId).ValueGeneratedOnAdd();

        b.Property(x => x.PeriodStart).HasColumnType("datetime2");
        b.Property(x => x.PeriodEnd).HasColumnType("datetime2");
        b.Property(x => x.DueDate).HasColumnType("datetime2");
        foreach (var money in new[] { nameof(CardStatement.OpeningBalance), nameof(CardStatement.Purchases),
                     nameof(CardStatement.CashWithdrawals), nameof(CardStatement.FeesAndCharges), nameof(CardStatement.Payments),
                     nameof(CardStatement.Refunds), nameof(CardStatement.Cashback), nameof(CardStatement.MovedToEmi),
                     nameof(CardStatement.ClosingBalance), nameof(CardStatement.MinimumDue),
                     nameof(CardStatement.EmiInstallmentsDue), nameof(CardStatement.PaidByDueDate) })
            b.Property(money).HasPrecision(18, 2);
        // Concurrency token: the scheduler and a manual statement run cannot both decide the outcome
        // (and charge the late fee twice) - the second save fails.
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired().IsConcurrencyToken();
        b.Property(x => x.AssessedAt).HasColumnType("datetime2");
        b.Property(x => x.ReminderSentAt).HasColumnType("datetime2");
        b.Ignore(x => x.IsAssessed);

        b.HasOne(x => x.Card).WithMany().HasForeignKey(x => x.CardId)
         .HasConstraintName("FK_CardStatements_CreditCards").OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CardId, x.PeriodEnd }).IsUnique().HasDatabaseName("UX_CardStatements_Card_PeriodEnd");
    }
}
