using Localll.Identity.API.Domain;
using Microsoft.EntityFrameworkCore;

namespace Localll.Identity.API.Data;

public class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<ApplicationUser> Users => Set<ApplicationUser>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationUser>(user =>
        {
            user.HasIndex(u => u.Email).IsUnique();
            user.HasIndex(u => u.PhoneNumber).IsUnique();
            user.Property(u => u.Email).HasMaxLength(256);
            user.Property(u => u.PhoneNumber).HasMaxLength(20);
            user.Property(u => u.FullName).HasMaxLength(200);
            user.Property(u => u.Role).HasMaxLength(50);
            user.Property(u => u.ApprovalStatus).HasConversion<string>().HasMaxLength(30);
            user.Property(u => u.ApplicationNote).HasMaxLength(1000);
            user.Property(u => u.RejectionReason).HasMaxLength(500);
            user.HasIndex(u => u.ApprovalStatus);
            user.HasIndex(u => u.GoogleSubject);
            user.HasMany(u => u.RefreshTokens).WithOne().HasForeignKey(t => t.UserId);
            user.Ignore(u => u.DomainEvents);
        });

        modelBuilder.Entity<RefreshToken>(token =>
        {
            token.HasIndex(t => t.Token).IsUnique();
        });
    }
}
