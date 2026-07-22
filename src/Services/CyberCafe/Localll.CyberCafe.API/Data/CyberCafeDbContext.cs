using Localll.CyberCafe.API.Domain;
using Microsoft.EntityFrameworkCore;

namespace Localll.CyberCafe.API.Data;

public class CyberCafeDbContext(DbContextOptions<CyberCafeDbContext> options) : DbContext(options)
{
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<SessionFile> Files => Set<SessionFile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Appointment>(appointment =>
        {
            appointment.HasIndex(a => a.CustomerId);
            appointment.HasIndex(a => a.ScheduledAtUtc);
            appointment.Property(a => a.Status).HasConversion<string>().HasMaxLength(30);
            appointment.HasMany(a => a.Files).WithOne().HasForeignKey(f => f.AppointmentId);
            appointment.Ignore(a => a.DomainEvents);
        });
    }
}
