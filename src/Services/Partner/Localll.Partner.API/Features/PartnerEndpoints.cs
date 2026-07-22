using System.Security.Claims;
using Localll.Common.Auth;
using Localll.Common.Caching;
using Localll.Contracts;
using Localll.Partner.API.Data;
using Localll.Partner.API.Domain;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Localll.Partner.API.Features;

public record RegisterPartnerRequest(string PartnerType, string Name, string City, string? LicenseNumber, string? VehicleNumber, string? KycDocumentUrl);
public record UpsertInventoryRequest(List<InventoryLine> Items);
public record InventoryLine(string MedicineName, decimal Price, int StockQuantity);
public record LocationUpdateRequest(double Latitude, double Longitude);

public static class PartnerEndpoints
{
    public static void MapPartnerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/partners").WithTags("Partners").RequireAuthorization();

        group.MapPost("/register", async (
            RegisterPartnerRequest request,
            ClaimsPrincipal principal,
            PartnerDbContext db,
            IPublishEndpoint publisher) =>
        {
            if (!Enum.TryParse<PartnerType>(request.PartnerType, true, out var type))
                return Results.BadRequest(new { error = "PartnerType must be Pharmacy or DeliveryPartner." });

            var userId = principal.GetUserId();
            if (await db.Partners.AnyAsync(p => p.UserId == userId))
                return Results.Conflict(new { error = "This account is already registered as a partner." });

            var partner = new Domain.Partner
            {
                UserId = userId,
                Type = type,
                Name = request.Name,
                City = request.City,
                LicenseNumber = request.LicenseNumber,
                VehicleNumber = request.VehicleNumber,
                KycDocumentUrl = request.KycDocumentUrl
            };
            db.Partners.Add(partner);
            await db.SaveChangesAsync();

            await publisher.Publish(new PartnerRegisteredEvent(
                partner.Id, type.ToString(), partner.Name, DateTime.UtcNow));

            return Results.Created($"/api/v1/partners/{partner.Id}", partner);
        });

        group.MapGet("/me", async (ClaimsPrincipal principal, PartnerDbContext db) =>
        {
            var partner = await db.Partners.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == principal.GetUserId());
            return partner is null ? Results.NotFound() : Results.Ok(partner);
        });

        group.MapPost("/me/online", async (ClaimsPrincipal principal, PartnerDbContext db, bool online = true) =>
        {
            var partner = await db.Partners.FirstOrDefaultAsync(p => p.UserId == principal.GetUserId());
            if (partner is null) return Results.NotFound();
            partner.IsOnline = online;
            partner.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { partner.IsOnline });
        });

        // Live location is transient — Redis only, 60s TTL.
        group.MapPost("/me/location", async (LocationUpdateRequest request, ClaimsPrincipal principal, ICacheService cache) =>
        {
            await cache.SetAsync($"partner:location:{principal.GetUserId()}", request, TimeSpan.FromSeconds(60));
            return Results.Accepted();
        }).RequireAuthorization(policy => policy.RequireRole("DeliveryPartner"));

        group.MapGet("/{partnerId:guid}/location", async (Guid partnerId, ICacheService cache) =>
        {
            var location = await cache.GetAsync<LocationUpdateRequest>($"partner:location:{partnerId}");
            return location is null ? Results.NotFound() : Results.Ok(location);
        });

        // Customer-facing partner card for live tracking: name & vehicle only.
        group.MapGet("/by-user/{userId:guid}", async (Guid userId, PartnerDbContext db) =>
        {
            var partner = await db.Partners.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId);
            return partner is null
                ? Results.NotFound()
                : Results.Ok(new
                {
                    name = partner.Name,
                    vehicleNumber = partner.VehicleNumber,
                    city = partner.City,
                    type = partner.Type.ToString(),
                });
        });

        // Delivery partner order feed.
        group.MapGet("/orders/available", async (PartnerDbContext db) =>
            Results.Ok(await db.AvailableOrders.AsNoTracking()
                .Where(o => o.AcceptedByPartnerId == null)
                .OrderByDescending(o => o.CreatedAtUtc)
                .Take(50).ToListAsync()))
            .RequireAuthorization(policy => policy.RequireRole("DeliveryPartner"));

        group.MapPost("/orders/{orderId:guid}/accept", async (
            Guid orderId,
            ClaimsPrincipal principal,
            PartnerDbContext db,
            IPublishEndpoint publisher) =>
        {
            var partnerId = principal.GetUserId();

            // Atomic claim — only one partner can win the race.
            var claimed = await db.AvailableOrders
                .Where(o => o.OrderId == orderId && o.AcceptedByPartnerId == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.AcceptedByPartnerId, partnerId)
                    .SetProperty(o => o.AcceptedAtUtc, DateTime.UtcNow));

            if (claimed == 0)
                return Results.Conflict(new { error = "Order was already accepted or does not exist." });

            await publisher.Publish(new OrderAcceptedEvent(orderId, partnerId, DateTime.UtcNow));
            return Results.Ok(new { orderId, partnerId });
        }).RequireAuthorization(policy => policy.RequireRole("DeliveryPartner"));

        // Pharmacy inventory.
        group.MapPut("/me/inventory", async (UpsertInventoryRequest request, ClaimsPrincipal principal, PartnerDbContext db) =>
        {
            var pharmacy = await db.Partners.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == principal.GetUserId() && p.Type == PartnerType.Pharmacy);
            if (pharmacy is null) return Results.NotFound(new { error = "No pharmacy registration found." });

            foreach (var line in request.Items)
            {
                var existing = await db.Inventory.FirstOrDefaultAsync(i =>
                    i.PharmacyId == pharmacy.Id && i.MedicineName == line.MedicineName);
                if (existing is null)
                    db.Inventory.Add(new InventoryItem
                    {
                        PharmacyId = pharmacy.Id,
                        MedicineName = line.MedicineName,
                        Price = line.Price,
                        StockQuantity = line.StockQuantity
                    });
                else
                {
                    existing.Price = line.Price;
                    existing.StockQuantity = line.StockQuantity;
                    existing.UpdatedAtUtc = DateTime.UtcNow;
                }
            }
            await db.SaveChangesAsync();
            return Results.Ok(new { updated = request.Items.Count });
        }).RequireAuthorization(policy => policy.RequireRole("PharmacyPartner"));

        group.MapGet("/me/inventory", async (ClaimsPrincipal principal, PartnerDbContext db) =>
        {
            var pharmacy = await db.Partners.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == principal.GetUserId() && p.Type == PartnerType.Pharmacy);
            if (pharmacy is null) return Results.NotFound(new { error = "No pharmacy registration found." });

            return Results.Ok(await db.Inventory.AsNoTracking()
                .Where(i => i.PharmacyId == pharmacy.Id)
                .OrderBy(i => i.MedicineName).ToListAsync());
        }).RequireAuthorization(policy => policy.RequireRole("PharmacyPartner"));
    }
}
