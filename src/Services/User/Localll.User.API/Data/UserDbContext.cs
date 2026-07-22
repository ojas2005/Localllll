using Localll.User.API.Domain;
using Microsoft.EntityFrameworkCore;

namespace Localll.User.API.Data;

public class UserDbContext(DbContextOptions<UserDbContext> options) : DbContext(options)
{
    public DbSet<CustomerProfile> Profiles => Set<CustomerProfile>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<Review> Reviews => Set<Review>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CustomerProfile>(profile =>
        {
            profile.HasIndex(p => p.Email).IsUnique();
            profile.HasMany(p => p.Addresses).WithOne().HasForeignKey(a => a.ProfileId);
            profile.Ignore(p => p.DomainEvents);
        });

        modelBuilder.Entity<Review>(review =>
        {
            review.HasIndex(r => new { r.TargetType, r.TargetId });
            review.Property(r => r.Rating).HasDefaultValue(5);
        });
    }
}
