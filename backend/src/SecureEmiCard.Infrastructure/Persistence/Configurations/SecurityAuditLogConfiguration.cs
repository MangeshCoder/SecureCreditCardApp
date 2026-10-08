using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SecureEmiCard.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SecureEmiCard.Infrastructure.Persistence.Configurations
{
    public class SecurityAuditLogConfiguration : IEntityTypeConfiguration<SecurityAuditLog>
    {
        public void Configure(EntityTypeBuilder<SecurityAuditLog> b)
        {
            // HasTrigger tells EF Core the table has a trigger (TR_SecurityAuditLogs_AppendOnly), so it does not use
            // "INSERT ... OUTPUT" (SQL Server does not allow OUTPUT without INTO on tables with triggers).
            b.ToTable("SecurityAuditLogs", t => t.HasTrigger("TR_SecurityAuditLogs_AppendOnly"));
            b.HasKey(x => x.AuditId);
            b.Property(x => x.AuditId).ValueGeneratedOnAdd();

            b.Property(x => x.Endpoint).HasMaxLength(250).IsRequired();
            b.Property(x => x.ActionType).HasMaxLength(50).IsRequired();
            b.Property(x => x.PayloadHash).HasMaxLength(256).IsRequired();
            b.Property(x => x.Timestamp).HasColumnType("datetime2");
            b.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(20).IsRequired();
            b.Property(x => x.PartnerId).HasMaxLength(50);
            b.Property(x => x.ClientIp).HasMaxLength(45);
            b.Property(x => x.CorrelationId).HasMaxLength(64);
            b.Property(x => x.Detail).HasMaxLength(250);

            b.HasIndex(x => x.Timestamp).HasDatabaseName("IX_SecurityAuditLogs_Timestamp");
            b.HasIndex(x => new { x.ActionType, x.Timestamp }).HasDatabaseName("IX_SecurityAuditLogs_ActionType_Timestamp");
            b.HasIndex(x => new { x.Outcome, x.Timestamp }).HasDatabaseName("IX_SecurityAuditLogs_Outcome_Timestamp");
        }
    }
}
