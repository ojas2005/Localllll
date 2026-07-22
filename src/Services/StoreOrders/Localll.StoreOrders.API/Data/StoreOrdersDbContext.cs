using Localll.StoreOrders.API.Domain;
using Microsoft.EntityFrameworkCore;

namespace Localll.StoreOrders.API.Data;

public class StoreOrdersDbContext(DbContextOptions<StoreOrdersDbContext> options) : DbContext(options)
{
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<StoreProduct> Products => Set<StoreProduct>();
    public DbSet<StoreOrder> Orders => Set<StoreOrder>();
    public DbSet<StoreOrderItem> OrderItems => Set<StoreOrderItem>();
    public DbSet<StoreWallet> Wallets => Set<StoreWallet>();
    public DbSet<StoreWalletEntry> WalletEntries => Set<StoreWalletEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Store>(store =>
        {
            store.Property(s => s.Name).HasMaxLength(120);
            store.HasIndex(s => s.OwnerUserId);
            store.HasMany(s => s.Products).WithOne().HasForeignKey(p => p.StoreId);
        });

        modelBuilder.Entity<StoreProduct>(product =>
        {
            product.HasIndex(p => p.StoreId);
            product.Property(p => p.Price).HasPrecision(10, 2);
        });

        modelBuilder.Entity<StoreOrder>(order =>
        {
            order.HasIndex(o => o.CustomerId);
            order.HasIndex(o => o.StoreId);
            order.HasIndex(o => o.Status);
            order.HasIndex(o => o.DeliveryPartnerId);
            order.Property(o => o.Status).HasConversion<string>().HasMaxLength(40);
            order.Property(o => o.PaymentMethod).HasConversion<string>().HasMaxLength(10);
            foreach (var money in new[] { nameof(StoreOrder.ItemsTotal), nameof(StoreOrder.ServiceCharge),
                     nameof(StoreOrder.DeliveryCharge), nameof(StoreOrder.GrandTotal),
                     nameof(StoreOrder.PlatformCommission), nameof(StoreOrder.StorePayout), nameof(StoreOrder.PartnerEarning) })
                order.Property(money).HasPrecision(12, 2);
            order.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId);
            order.Ignore(o => o.DomainEvents);
        });

        modelBuilder.Entity<StoreOrderItem>(item =>
        {
            item.Ignore(i => i.LineTotal);
            item.Property(i => i.UnitPrice).HasPrecision(10, 2);
        });

        modelBuilder.Entity<StoreWallet>(wallet =>
        {
            wallet.HasIndex(w => w.StoreId).IsUnique();
            wallet.Property(w => w.Balance).HasPrecision(14, 2);
            wallet.HasMany(w => w.Entries).WithOne().HasForeignKey(e => e.WalletId);
            wallet.Ignore(w => w.DomainEvents);
        });

        modelBuilder.Entity<StoreWalletEntry>(entry =>
        {
            entry.HasIndex(e => e.WalletId);
            entry.Property(e => e.Type).HasConversion<string>().HasMaxLength(10);
            entry.Property(e => e.Amount).HasPrecision(14, 2);
            entry.Property(e => e.BalanceAfter).HasPrecision(14, 2);
        });

        SeedCatalog(modelBuilder);
    }

    private static void SeedCatalog(ModelBuilder modelBuilder)
    {
        var seededAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var freshMart = Guid.Parse("33333333-0000-0000-0000-000000000001");
        var medPlus = Guid.Parse("33333333-0000-0000-0000-000000000002");

        modelBuilder.Entity<Store>().HasData(
            new Store { Id = freshMart, Name = "FreshMart Grocery", Address = "Shop 4, Hazratganj Market", City = "Lucknow", UpiId = "freshmart@okaxis", CreatedAtUtc = seededAt },
            new Store { Id = medPlus, Name = "MedPlus Pharmacy", Address = "12 Station Road", City = "Lucknow", UpiId = "medplus@okhdfcbank", CreatedAtUtc = seededAt });

        StoreProduct P(string id, Guid storeId, string name, string category, string unit, decimal price) =>
            new() { Id = Guid.Parse(id), StoreId = storeId, Name = name, Category = category, UnitLabel = unit, Price = price, CreatedAtUtc = seededAt };

        modelBuilder.Entity<StoreProduct>().HasData(
            P("44444444-0000-0000-0000-000000000001", freshMart, "Basmati Rice", "Staples", "1 kg", 95m),
            P("44444444-0000-0000-0000-000000000002", freshMart, "Wheat Atta", "Staples", "5 kg", 240m),
            P("44444444-0000-0000-0000-000000000003", freshMart, "Full Cream Milk", "Dairy", "1 L", 66m),
            P("44444444-0000-0000-0000-000000000004", freshMart, "Fresh Tomatoes", "Produce", "1 kg", 45m),
            P("44444444-0000-0000-0000-000000000005", freshMart, "Sunflower Oil", "Staples", "1 L", 130m),
            P("44444444-0000-0000-0000-000000000006", medPlus, "Paracetamol 500mg", "Medicine", "10 tablets", 30m),
            P("44444444-0000-0000-0000-000000000007", medPlus, "Cetirizine 10mg", "Medicine", "10 tablets", 25m),
            P("44444444-0000-0000-0000-000000000008", medPlus, "Digital Thermometer", "Devices", "1 unit", 180m),
            P("44444444-0000-0000-0000-000000000009", medPlus, "Antiseptic Liquid", "First Aid", "500 ml", 120m));
    }
}
