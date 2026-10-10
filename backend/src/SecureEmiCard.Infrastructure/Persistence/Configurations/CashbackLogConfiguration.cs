using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Infrastructure.Persistence.Configurations;

public class CashbackLogConfiguration : IEntityTypeConfiguration<CashbackLog>
{
    public void Configure(EntityTypeBuilder<CashbackLog> b)
    {
        b.ToTable("CashbackLogs");
        b.HasKey(x => x.CashbackId);
        b.Property(x => x.CashbackId).ValueGeneratedOnAdd();

        b.Property(x => x.CashbackPercentage).HasPrecision(5, 2);
        b.Property(x => x.CashbackAmount).HasPrecision(18, 2);
        b.Property(x => x.CashbackType).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.CreditedDate).HasColumnType("datetime2");

        // Module 8: which statement billed this row (null = not billed yet).
        b.HasOne(x => x.Statement).WithMany().HasForeignKey(x => x.StatementId).IsRequired(false)
         .HasConstraintName("FK_CashbackLogs_CardStatements").OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Transaction)
         .WithMany()
         .HasForeignKey(x => x.TransactionId)
         .HasConstraintName("FK_CashbackLogs_Transactions")
         .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<CreditCard>()
         .WithMany()
         .HasForeignKey(x => x.CardId)
         .HasConstraintName("FK_CashbackLogs_CreditCards")
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.TransactionId, x.CashbackType }).IsUnique().HasDatabaseName("UQ_CashbackLogs_Transaction_Type");
        b.HasIndex(x => new { x.CardId, x.CreditedDate }).HasDatabaseName("IX_CashbackLogs_CardId_Date");
    }
}
