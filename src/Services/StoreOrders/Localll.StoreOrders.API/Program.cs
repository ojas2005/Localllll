using Localll.Common.Extensions;
using Localll.StoreOrders.API.Data;
using Localll.StoreOrders.API.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatform("store-orders-service");
builder.Services.AddDbContext<StoreOrdersDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Database")));

// Allow up to 10 MB screenshot uploads.
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 10 * 1024 * 1024);

var app = builder.Build();

var uploadsRoot = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "uploads");
Directory.CreateDirectory(uploadsRoot);

app.UsePlatform();

// Serve uploaded screenshots statically (anonymous — img tags can't send tokens).
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsRoot),
    RequestPath = "/api/v1/store/uploads",
});

app.MapStoreOrderEndpoints();
app.MapUploadEndpoints(uploadsRoot);

await app.InitializeDatabaseAsync<StoreOrdersDbContext>();
app.Run();
