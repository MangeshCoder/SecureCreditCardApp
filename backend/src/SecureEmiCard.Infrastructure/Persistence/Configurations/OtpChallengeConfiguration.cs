using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Infrastructure.Persistence.Configurations;

public class OtpChallengeConfiguration : IEntityTypeConfiguration<OtpChallenge>
{
    public void Configure(EntityTypeBuilder<OtpChallenge> b)
    {
        b.ToTable("OtpChallenges");
        b.HasKey(x => x.OtpChallengeId);
        b.Property(x => x.OtpChallengeId).ValueGeneratedOnAdd();

        b.Property(x => x.Purpose).HasConversion<string>().HasMaxLength(30).IsRequired();
        b.Property(x => x.ContextHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.CodeHash).HasMaxLength(256).IsRequired();
        b.Property(x => x.SentTo).HasMaxLength(30).IsRequired();
        // Concurrency tokens: two requests racing with the same code (or two wrong guesses at once)
        // cannot both be counted from the same starting state - one save fails.
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired().IsConcurrencyToken();
        b.Property(x => x.Attempts).IsConcurrencyToken();
        b.Property(x => x.CreatedAt).HasColumnType("datetime2");
        b.Property(x => x.ExpiresAt).HasColumnType("datetime2");
        b.Property(x => x.UsedAt).HasColumnType("datetime2");
        b.Ignore(x => x.RemainingAttempts);

        b.HasOne<Cardholder>().WithMany().HasForeignKey(x => x.CardholderId)
         .HasConstraintName("FK_OtpChallenges_Cardholders").OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CardholderId, x.CreatedAt }).HasDatabaseName("IX_OtpChallenges_Cardholder_Created");
    }
}
