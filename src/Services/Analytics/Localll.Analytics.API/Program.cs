using Localll.Analytics.API.Consumers;
using Localll.Analytics.API.Data;
using Localll.Analytics.API.Features;
using Localll.Common.Extensions;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatform("analytics-service", bus => bus.AddConsumer<MetricConsumers>());
builder.Services.AddDbContext<AnalyticsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Database")));

var app = builder.Build();

app.UsePlatform();
app.MapAnalyticsEndpoints();

await app.InitializeDatabaseAsync<AnalyticsDbContext>();
app.Run();
