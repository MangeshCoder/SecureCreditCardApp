using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Infrastructure.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("Notifications");
        b.HasKey(x => x.NotificationId);
        b.Property(x => x.NotificationId).ValueGeneratedOnAdd();

        b.Property(x => x.Category).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Title).HasMaxLength(Notification.MaxTitleLength).IsRequired();
        b.Property(x => x.Message).HasMaxLength(Notification.MaxMessageLength).IsRequired();
        b.Property(x => x.CreatedAt).HasColumnType("datetime2");
        b.Property(x => x.ReadAt).HasColumnType("datetime2");
        b.Property(x => x.DeliveryStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.SentAt).HasColumnType("datetime2");
        b.Ignore(x => x.IsRead);

        b.HasOne(x => x.Cardholder).WithMany().HasForeignKey(x => x.CardholderId)
         .HasConstraintName("FK_Notifications_Cardholders").OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Card).WithMany().HasForeignKey(x => x.CardId).IsRequired(false)
         .HasConstraintName("FK_Notifications_CreditCards").OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.CardholderId, x.CreatedAt }).HasDatabaseName("IX_Notifications_Cardholder_Created");
    }
}
