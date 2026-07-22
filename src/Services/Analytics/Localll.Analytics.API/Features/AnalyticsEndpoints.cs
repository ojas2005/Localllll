using Localll.Analytics.API.Data;
using Microsoft.EntityFrameworkCore;

namespace Localll.Analytics.API.Features;

public static class AnalyticsEndpoints
{
    public static void MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/analytics").WithTags("Analytics")
            .RequireAuthorization(policy => policy.RequireRole("Admin"));

        group.MapGet("/dashboard", async (AnalyticsDbContext db, int days = 30) =>
        {
            var since = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-days));
            var metrics = await db.DailyMetrics.AsNoTracking()
                .Where(m => m.Date >= since)
                .GroupBy(m => m.Metric)
                .Select(g => new { Metric = g.Key, Total = g.Sum(m => m.Value) })
                .ToListAsync();
            return Results.Ok(new { rangeDays = days, metrics });
        });

        group.MapGet("/daily/{metric}", async (string metric, AnalyticsDbContext db, int days = 30) =>
        {
            var since = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-days));
            var series = await db.DailyMetrics.AsNoTracking()
                .Where(m => m.Metric == metric && m.Date >= since)
                .OrderBy(m => m.Date)
                .Select(m => new { m.Date, m.Value })
                .ToListAsync();
            return Results.Ok(series);
        });
    }
}
