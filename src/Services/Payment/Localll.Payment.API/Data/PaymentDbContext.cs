using Microsoft.EntityFrameworkCore;

namespace Localll.Payment.API.Data;

public class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<Domain.Payment> Payments => Set<Domain.Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Domain.Payment>(payment =>
        {
            // The idempotency key makes retried payment requests safe.
            payment.HasIndex(p => p.IdempotencyKey).IsUnique();
            payment.HasIndex(p => p.OrderId);
            payment.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
            payment.Property(p => p.Method).HasMaxLength(20);
            payment.Property(p => p.Amount).HasPrecision(12, 2);
            payment.Ignore(p => p.DomainEvents);
        });
    }
}
