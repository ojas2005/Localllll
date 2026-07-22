using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Localll.Common.Extensions;

public static class DatabaseExtensions
{
    /// <summary>
    /// Creates the service's schema on startup with a small retry loop so
    /// services survive Postgres coming up slightly later in docker-compose.
    /// Production deployments should switch to EF Core migrations in CI/CD.
    /// </summary>
    public static async Task InitializeDatabaseAsync<TContext>(this WebApplication app)
        where TContext : DbContext
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TContext>>();

        const int maxAttempts = 10;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await db.Database.EnsureCreatedAsync();
                logger.LogInformation("Database ready for {Context}", typeof(TContext).Name);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                logger.LogWarning(ex, "Database not ready (attempt {Attempt}/{Max}), retrying…", attempt, maxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
            catch (Exception ex)
            {
                // Don't crash the service in dev when infra isn't running; endpoints will fail loudly instead.
                logger.LogError(ex, "Could not initialize database for {Context}", typeof(TContext).Name);
                return;
            }
        }
    }
}
