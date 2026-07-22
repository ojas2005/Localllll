using Localll.Common.Extensions;
using Localll.User.API.Consumers;
using Localll.User.API.Data;
using Localll.User.API.Features;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatform("user-service", bus => bus.AddConsumer<UserRegisteredConsumer>());
builder.Services.AddDbContext<UserDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Database")));

var app = builder.Build();

app.UsePlatform();
app.MapUserEndpoints();

await app.InitializeDatabaseAsync<UserDbContext>();
app.Run();
