using Localll.Common.Extensions;
using Localll.CyberCafe.API.Data;
using Localll.CyberCafe.API.Features;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatform("cybercafe-service");
builder.Services.AddDbContext<CyberCafeDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Database")));

var app = builder.Build();

app.UsePlatform();
app.MapCyberCafeEndpoints();

await app.InitializeDatabaseAsync<CyberCafeDbContext>();
app.Run();
