using Localll.Common.Extensions;
using Localll.Delivery.API.Consumers;
using Localll.Delivery.API.Data;
using Localll.Delivery.API.Features;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatform("delivery-service", bus =>
{
    bus.AddConsumer<PaymentCompletedConsumer>();
    bus.AddConsumer<OrderAcceptedConsumer>();
});
builder.Services.AddDbContext<DeliveryDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Database")));

var app = builder.Build();

app.UsePlatform();
app.MapDeliveryEndpoints();

await app.InitializeDatabaseAsync<DeliveryDbContext>();
app.Run();
