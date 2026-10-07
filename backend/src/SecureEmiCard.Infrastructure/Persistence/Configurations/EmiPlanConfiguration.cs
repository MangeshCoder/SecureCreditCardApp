using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Infrastructure.Persistence.Configurations;

public class EmiPlanConfiguration : IEntityTypeConfiguration<EmiPlan>
{
    public void Configure(EntityTypeBuilder<EmiPlan> b)
    {
        b.ToTable("EmiPlans");
        b.HasKey(x => x.EmiPlanId);
        b.Property(x => x.EmiPlanId).ValueGeneratedOnAdd();

        b.Property(x => x.PrincipalAmount).HasPrecision(18, 2);
        b.Property(x => x.AnnualInterestRate).HasPrecision(5, 2);
        b.Property(x => x.MonthlyInstallment).HasPrecision(18, 2);
        b.Property(x => x.TotalRepayable).HasPrecision(18, 2);
        b.Property(x => x.RemainingBalance).HasPrecision(18, 2);
        b.Property(x => x.PlanStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.CreatedDate).HasColumnType("datetime2");

        b.HasOne(x => x.Transaction)
         .WithMany()
         .HasForeignKey(x => x.TransactionId)
         .HasConstraintName("FK_EmiPlans_Transactions")
         .OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.TransactionId).IsUnique().HasDatabaseName("UQ_EmiPlans_TransactionId");

        b.HasOne<CreditCard>()
         .WithMany()
         .HasForeignKey(x => x.CardId)
         .HasConstraintName("FK_EmiPlans_CreditCards")
         .OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.CardId).HasDatabaseName("IX_EmiPlans_CardId");

        b.HasMany(x => x.Schedules)
         .WithOne()
         .HasForeignKey(s => s.EmiPlanId)
         .HasConstraintName("FK_EmiSchedules_EmiPlans")
         .OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Schedules).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Ignore(x => x.TotalInterest);
        b.Ignore(x => x.OutstandingPrincipal);
        b.Ignore(x => x.PaidInstallments);
        b.Ignore(x => x.NextInstallment);
    }
}

public class EmiScheduleConfiguration : IEntityTypeConfiguration<EmiSchedule>
{
    public void Configure(EntityTypeBuilder<EmiSchedule> b)
    {
        b.ToTable("EmiSchedules");
        b.HasKey(x => x.ScheduleId);
        b.Property(x => x.ScheduleId).ValueGeneratedOnAdd();

        b.Property(x => x.DueDate).HasColumnType("date");
        b.Property(x => x.AmountDue).HasPrecision(18, 2);
        b.Property(x => x.PrincipalComponent).HasPrecision(18, 2);
        b.Property(x => x.InterestComponent).HasPrecision(18, 2);
        b.Property(x => x.PaymentStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.PaidDate).HasColumnType("datetime2");

        b.HasOne(x => x.PaymentTransaction)
         .WithMany()
         .HasForeignKey(x => x.PaymentTransactionId)
         .HasConstraintName("FK_EmiSchedules_Transactions")
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.EmiPlanId, x.InstallmentNumber }).IsUnique().HasDatabaseName("UQ_EmiSchedules_Plan_Number");
        b.HasIndex(x => new { x.EmiPlanId, x.PaymentStatus }).HasDatabaseName("IX_EmiSchedules_Plan_Status");

        b.Ignore(x => x.IsPaid);
    }
}
