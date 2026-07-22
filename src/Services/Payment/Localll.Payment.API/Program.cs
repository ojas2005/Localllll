using Localll.Common.Extensions;
using Localll.Payment.API.Data;
using Localll.Payment.API.Features;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatform("payment-service");
builder.Services.AddDbContext<PaymentDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Database")));

var app = builder.Build();

app.UsePlatform();
app.MapPaymentEndpoints();

await app.InitializeDatabaseAsync<PaymentDbContext>();
app.Run();
