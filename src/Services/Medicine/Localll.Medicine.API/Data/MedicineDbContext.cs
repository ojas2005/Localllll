using Localll.Medicine.API.Domain;
using Microsoft.EntityFrameworkCore;

namespace Localll.Medicine.API.Data;

public class MedicineDbContext(DbContextOptions<MedicineDbContext> options) : DbContext(options)
{
    public DbSet<Domain.Medicine> Medicines => Set<Domain.Medicine>();
    public DbSet<MedicineOrder> Orders => Set<MedicineOrder>();
    public DbSet<MedicineOrderItem> OrderItems => Set<MedicineOrderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Domain.Medicine>(medicine =>
        {
            medicine.HasIndex(m => m.Name);
            medicine.Property(m => m.Price).HasPrecision(12, 2);
        });

        modelBuilder.Entity<MedicineOrder>(order =>
        {
            order.HasIndex(o => o.CustomerId);
            order.HasIndex(o => o.Status);
            order.Property(o => o.Status).HasConversion<string>().HasMaxLength(30);
            order.Property(o => o.TotalAmount).HasPrecision(12, 2);
            order.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId);
            order.Ignore(o => o.DomainEvents);
        });

        modelBuilder.Entity<MedicineOrderItem>(item =>
        {
            item.Property(i => i.UnitPrice).HasPrecision(12, 2);
        });

        // Seed a small catalog so search works out of the box in dev.
        modelBuilder.Entity<Domain.Medicine>().HasData(
            new Domain.Medicine { Id = Guid.Parse("11111111-0000-0000-0000-000000000001"), Name = "Paracetamol 500mg", GenericName = "Acetaminophen", Manufacturer = "Cipla", Price = 30m, RequiresPrescription = false, Category = "Pain Relief", CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Domain.Medicine { Id = Guid.Parse("11111111-0000-0000-0000-000000000002"), Name = "Amoxicillin 250mg", GenericName = "Amoxicillin", Manufacturer = "Sun Pharma", Price = 90m, RequiresPrescription = true, Category = "Antibiotic", CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Domain.Medicine { Id = Guid.Parse("11111111-0000-0000-0000-000000000003"), Name = "Cetirizine 10mg", GenericName = "Cetirizine", Manufacturer = "Dr. Reddy's", Price = 25m, RequiresPrescription = false, Category = "Allergy", CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
    }
}
