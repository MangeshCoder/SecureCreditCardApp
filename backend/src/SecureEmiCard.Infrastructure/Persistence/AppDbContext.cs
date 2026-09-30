using Microsoft.EntityFrameworkCore;
using SecureEmiCard.Application.Abstractions.Persistence;
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
