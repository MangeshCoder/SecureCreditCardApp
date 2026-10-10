using Microsoft.EntityFrameworkCore;
using SecureEmiCard.Domain.Entities;
using SecureEmiCard.Domain.Enums;
using SecureEmiCard.Infrastructure.Persistence;

namespace SecureEmiCard.UnitTests.Infrastructure;

public class UtcDateTimeTests
{
    [Fact]
    public async Task Dates_read_from_the_database_are_marked_utc()
    {
        // SQL Server's DATETIME2 forgets the "Kind"; the context's convention must put UTC back on read.
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var unspecified = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Unspecified);

        await using (var db = new AppDbContext(options))
        {
            var alert = Notification.ForCardholder(1, NotificationCategory.Security, "t", "m");
            alert.MarkRead(unspecified);
            db.Notifications.Add(alert);
            await db.SaveChangesAsync();
        }

        await using (var db = new AppDbContext(options))
        {
            var alert = await db.Notifications.SingleAsync();
            Assert.Equal(DateTimeKind.Utc, alert.ReadAt!.Value.Kind);
            Assert.Equal(DateTimeKind.Utc, alert.CreatedAt.Kind);
            Assert.Equal(unspecified.Ticks, alert.ReadAt.Value.Ticks);   // same moment, only the Kind is fixed
        }
    }
}
