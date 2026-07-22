using Localll.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Localll.Analytics.API.Data;

/// <summary>Daily rollup, keyed by (date, metric). Read-optimized for dashboards.</summary>
public class DailyMetric : Entity
{
    public DateOnly Date { get; set; }
    public string Metric { get; set; } = string.Empty;  // OrdersCreated | Revenue | DeliveriesCompleted | NewUsers | NewPartners
    public decimal Value { get; set; }
}

public class AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options) : DbContext(options)
{
    public DbSet<DailyMetric> DailyMetrics => Set<DailyMetric>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DailyMetric>(metric =>
        {
            metric.HasIndex(m => new { m.Date, m.Metric }).IsUnique();
            metric.Property(m => m.Metric).HasMaxLength(50);
            metric.Property(m => m.Value).HasPrecision(16, 2);
        });
    }
}
