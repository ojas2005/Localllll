using Localll.Common.Extensions;
using Localll.Partner.API.Consumers;
using Localll.Partner.API.Data;
using Localll.Partner.API.Features;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatform("partner-service", bus =>
{
    bus.AddConsumer<OrderCreatedProjectionConsumer>();
    bus.AddConsumer<PartnerUserRegisteredConsumer>();
    bus.AddConsumer<PartnerDeliveryCompletedConsumer>();
});
builder.Services.AddDbContext<PartnerDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Database")));

var app = builder.Build();

app.UsePlatform();
app.MapPartnerEndpoints();

await app.InitializeDatabaseAsync<PartnerDbContext>();
app.Run();
