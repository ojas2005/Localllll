using Localll.Common.Extensions;
using Localll.Medicine.API.Consumers;
using Localll.Medicine.API.Data;
using Localll.Medicine.API.Features;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatform("medicine-service", bus => bus.AddConsumer<MedicinePaymentConsumer>());
builder.Services.AddDbContext<MedicineDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Database")));

var app = builder.Build();

app.UsePlatform();
app.MapMedicineEndpoints();

await app.InitializeDatabaseAsync<MedicineDbContext>();
app.Run();
