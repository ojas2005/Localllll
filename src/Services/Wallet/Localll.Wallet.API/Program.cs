using Localll.Common.Extensions;
using Localll.Wallet.API.Consumers;
using Localll.Wallet.API.Data;
using Localll.Wallet.API.Features;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatform("wallet-service", bus =>
{
    bus.AddConsumer<DeliveryCompletedConsumer>();
    bus.AddConsumer<WalletUserRegisteredConsumer>();
});
builder.Services.AddDbContext<WalletDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Database")));

var app = builder.Build();

app.UsePlatform();
app.MapWalletEndpoints();

await app.InitializeDatabaseAsync<WalletDbContext>();
app.Run();
