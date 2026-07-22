using Localll.Delivery.API.Domain;
using Microsoft.EntityFrameworkCore;

namespace Localll.Delivery.API.Data;

public class DeliveryDbContext(DbContextOptions<DeliveryDbContext> options) : DbContext(options)
{
    public DbSet<DeliveryOrder> Orders => Set<DeliveryOrder>();
    public DbSet<TrackingEvent> TrackingEvents => Set<TrackingEvent>();
    public DbSet<GroceryItem> GroceryItems => Set<GroceryItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DeliveryOrder>(order =>
        {
            order.HasIndex(o => o.CustomerId);
            order.HasIndex(o => o.Status);
            order.Property(o => o.Status).HasConversion<string>().HasMaxLength(30);
            order.Property(o => o.OrderType).HasConversion<string>().HasMaxLength(20);
            order.Property(o => o.Charge).HasPrecision(12, 2);
            order.Ignore(o => o.DomainEvents);
        });

        modelBuilder.Entity<TrackingEvent>(tracking =>
        {
            tracking.HasIndex(t => t.OrderId);
        });

        modelBuilder.Entity<GroceryItem>(item =>
        {
            item.HasIndex(i => i.Category);
            item.Property(i => i.Name).HasMaxLength(120);
            item.Property(i => i.Category).HasMaxLength(40);
            item.Property(i => i.UnitLabel).HasMaxLength(20);
            item.Property(i => i.Price).HasPrecision(10, 2);
        });

        // Seed a starter catalog so the grocery cart works out of the box in dev.
        var seededAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        GroceryItem Seed(string id, string name, string category, string unit, decimal price, double kg) =>
            new() { Id = Guid.Parse(id), Name = name, Category = category, UnitLabel = unit, Price = price, WeightKg = kg, CreatedAtUtc = seededAt };

        modelBuilder.Entity<GroceryItem>().HasData(
            Seed("22222222-0000-0000-0000-000000000001", "Basmati Rice", "Staples", "1 kg", 95m, 1.0),
            Seed("22222222-0000-0000-0000-000000000002", "Wheat Atta", "Staples", "5 kg", 240m, 5.0),
            Seed("22222222-0000-0000-0000-000000000003", "Toor Dal", "Staples", "1 kg", 140m, 1.0),
            Seed("22222222-0000-0000-0000-000000000004", "Sunflower Oil", "Staples", "1 L", 130m, 0.95),
            Seed("22222222-0000-0000-0000-000000000005", "Full Cream Milk", "Dairy", "1 L", 66m, 1.05),
            Seed("22222222-0000-0000-0000-000000000006", "Paneer", "Dairy", "200 g", 90m, 0.22),
            Seed("22222222-0000-0000-0000-000000000007", "Curd", "Dairy", "400 g", 35m, 0.42),
            Seed("22222222-0000-0000-0000-000000000008", "Potatoes", "Produce", "1 kg", 32m, 1.0),
            Seed("22222222-0000-0000-0000-000000000009", "Onions", "Produce", "1 kg", 38m, 1.0),
            Seed("22222222-0000-0000-0000-000000000010", "Tomatoes", "Produce", "1 kg", 45m, 1.0),
            Seed("22222222-0000-0000-0000-000000000011", "Bananas", "Produce", "1 dozen", 55m, 1.4),
            Seed("22222222-0000-0000-0000-000000000012", "Parle-G Biscuits", "Snacks", "800 g", 80m, 0.85),
            Seed("22222222-0000-0000-0000-000000000013", "Namkeen Mix", "Snacks", "400 g", 95m, 0.42),
            Seed("22222222-0000-0000-0000-000000000014", "Detergent Powder", "Household", "1 kg", 110m, 1.0),
            Seed("22222222-0000-0000-0000-000000000015", "Dishwash Bar", "Household", "3 pack", 60m, 0.45));
    }
}
