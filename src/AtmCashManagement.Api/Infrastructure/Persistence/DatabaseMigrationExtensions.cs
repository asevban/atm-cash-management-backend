using Microsoft.EntityFrameworkCore;

namespace AtmCashManagement.Api.Infrastructure.Persistence;

public static class DatabaseMigrationExtensions
{
    public static async Task ApplyDatabaseMigrationsAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
        await dbContext.Database.MigrateAsync();
    }
}
