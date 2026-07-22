using Localll.Common.Extensions;
using Localll.Identity.API.Data;
using Localll.Identity.API.Domain;
using Localll.Identity.API.Features;
using Localll.Identity.API.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatform("identity-service");
builder.Services.AddDbContext<IdentityDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Database")));
builder.Services.AddScoped<TokenService>();

var app = builder.Build();

app.UsePlatform();
app.MapAuthEndpoints();
app.MapPartnerApprovalEndpoints();

await app.InitializeDatabaseAsync<IdentityDbContext>();
await SeedOwnerAsync(app);
app.Run();

/// <summary>Seeds the platform owner so partner applications can be reviewed out of the box.</summary>
static async Task SeedOwnerAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        if (await db.Users.AnyAsync(u => u.Role == Roles.Admin)) return;

        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var email = config["Owner:Email"] ?? "owner@localll.in";
        var password = config["Owner:Password"] ?? "Owner@123";

        db.Users.Add(new ApplicationUser
        {
            Email = email,
            PhoneNumber = "9999999999",
            FullName = "Localll Owner",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = Roles.Admin,
            ApprovalStatus = ApprovalStatus.Approved,
            EmailVerified = true,
            PhoneVerified = true
        });
        await db.SaveChangesAsync();
        logger.LogInformation("Seeded owner account {Email} (change the default password!)", email);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Could not seed the owner account");
    }
}
