using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SecureEmiCard.Application.Abstractions.Persistence;
using SecureEmiCard.Application.Common.Exceptions;
using SecureEmiCard.Domain.Entities;

namespace SecureEmiCard.Infrastructure.Persistence;

/// <summary>
/// EF Core context for SecureEmiCardDb. The SQL scripts in /database are the source of truth
/// for the schema; the entity configurations map exactly onto those tables and columns.
/// The DbContext is also the Unit of Work: SaveChangesAsync commits everything in one transaction.
/// </summary>
public class AppDbContext : DbContext, IUnitOfWork
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Cardholder> Cardholders => Set<Cardholder>();
    public DbSet<CreditCard> CreditCards => Set<CreditCard>();
    public DbSet<CardControl> CardControls => Set<CardControl>();
    public DbSet<CardTransaction> Transactions => Set<CardTransaction>();
    public DbSet<CashbackLog> CashbackLogs => Set<CashbackLog>();
    public DbSet<EmiPlan> EmiPlans => Set<EmiPlan>();
    public DbSet<EmiSchedule> EmiSchedules => Set<EmiSchedule>();
    public DbSet<SecurityAuditLog> SecurityAuditLogs => Set<SecurityAuditLog>();
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    /// <summary>
    /// Every DateTime in this database is UTC, but SQL Server's DATETIME2 does not store that, so values come
    /// back as "Unspecified" and the JSON has no "Z" - browsers then show UTC times as local times.
    /// This marks them as UTC when read (writes are unchanged).
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

    /// <summary>Translates EF's concurrency exception into the application's own exception type.</summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("The record was modified by another request. Please retry.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // 2601/2627 = unique index / unique constraint violation, e.g. two requests converting the
            // same purchase to EMI at the same moment (UQ_EmiPlans_TransactionId). Answer 409, not 500.
            throw new ConflictException("This operation was already completed by another request.");
        }
    }

    public void ClearChanges() => ChangeTracker.Clear();
}
