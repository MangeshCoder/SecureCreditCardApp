using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Infrastructure.Persistence.Configurations;

public class CardholderConfiguration : IEntityTypeConfiguration<Cardholder>
{
    public void Configure(EntityTypeBuilder<Cardholder> b)
    {
        b.ToTable("Cardholders");
        b.HasKey(x => x.CardholderId);
        b.Property(x => x.CardholderId).ValueGeneratedOnAdd();

        b.Property(x => x.FirstName).HasMaxLength(50).IsRequired();
        b.Property(x => x.LastName).HasMaxLength(50).IsRequired();
        b.Property(x => x.Email).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Email).IsUnique();
        b.Property(x => x.PhoneNumber).HasMaxLength(20).IsRequired();
        b.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
        b.Property(x => x.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.CreatedAt).HasColumnType("datetime2");
        b.Property(x => x.IsActive);

        b.Ignore(x => x.FullName);

        b.HasMany(x => x.Cards)
         .WithOne(c => c.Cardholder)
         .HasForeignKey(c => c.CardholderId)
         .HasConstraintName("FK_CreditCards_Cardholders")
         .OnDelete(DeleteBehavior.Restrict);

        // Cards is exposed as IReadOnlyCollection; EF writes to the private _cards field.
        b.Navigation(x => x.Cards).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
