using Microsoft.EntityFrameworkCore;

namespace Localll.Wallet.API.Data;

public class WalletDbContext(DbContextOptions<WalletDbContext> options) : DbContext(options)
{
    public DbSet<Domain.Wallet> Wallets => Set<Domain.Wallet>();
    public DbSet<Domain.LedgerEntry> LedgerEntries => Set<Domain.LedgerEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Domain.Wallet>(wallet =>
        {
            wallet.HasIndex(w => w.OwnerId).IsUnique();
            wallet.Property(w => w.Balance).HasPrecision(14, 2);
            wallet.Property(w => w.OwnerType).HasMaxLength(20);
            wallet.HasMany(w => w.Entries).WithOne().HasForeignKey(e => e.WalletId);
            wallet.Ignore(w => w.DomainEvents);
        });

        modelBuilder.Entity<Domain.LedgerEntry>(entry =>
        {
            entry.HasIndex(e => e.WalletId);
            entry.HasIndex(e => e.ReferenceId);
            entry.Property(e => e.Type).HasConversion<string>().HasMaxLength(10);
            entry.Property(e => e.Amount).HasPrecision(14, 2);
            entry.Property(e => e.BalanceAfter).HasPrecision(14, 2);
        });
    }
}
