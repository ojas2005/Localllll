using Localll.Partner.API.Domain;
using Microsoft.EntityFrameworkCore;

namespace Localll.Partner.API.Data;

public class PartnerDbContext(DbContextOptions<PartnerDbContext> options) : DbContext(options)
{
    public DbSet<Domain.Partner> Partners => Set<Domain.Partner>();
    public DbSet<InventoryItem> Inventory => Set<InventoryItem>();
    public DbSet<AvailableOrder> AvailableOrders => Set<AvailableOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Domain.Partner>(partner =>
        {
            partner.HasIndex(p => p.UserId).IsUnique();
            partner.Property(p => p.Type).HasConversion<string>().HasMaxLength(20);
            partner.Property(p => p.Status).HasConversion<string>().HasMaxLength(30);
            partner.Property(p => p.TotalEarnings).HasPrecision(14, 2);
            partner.Ignore(p => p.DomainEvents);
        });

        modelBuilder.Entity<InventoryItem>(item =>
        {
            item.HasIndex(i => i.PharmacyId);
            item.HasIndex(i => i.MedicineName);
            item.Property(i => i.Price).HasPrecision(12, 2);
        });

        modelBuilder.Entity<AvailableOrder>(order =>
        {
            order.HasIndex(o => o.OrderId).IsUnique();
            order.HasIndex(o => o.AcceptedByPartnerId);
            order.Property(o => o.Amount).HasPrecision(12, 2);
        });
    }
}
