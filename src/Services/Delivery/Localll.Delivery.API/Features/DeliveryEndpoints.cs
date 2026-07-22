using System.Security.Claims;
using System.Security.Cryptography;
using Localll.Common.Auth;
using Localll.Common.Caching;
using Localll.Contracts;
using Localll.Delivery.API.Data;
using Localll.Delivery.API.Domain;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Localll.Delivery.API.Features;

public record CreateDeliveryOrderRequest(
    string OrderType,          // Parcel | Grocery
    string PickupAddress,
    string DropAddress,
    double DistanceKm,
    double WeightKg,
    string? ItemsDescription);

public record QuoteRequest(string OrderType, double DistanceKm, double WeightKg);
public record CompleteDeliveryRequest(string Otp);

public static class DeliveryEndpoints
{
    private static readonly TimeSpan OtpTtl = TimeSpan.FromHours(6);

    public static void MapDeliveryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/deliveries").WithTags("Deliveries").RequireAuthorization();

        // Price quote — cached per (type, distance, weight) bucket.
        group.MapPost("/quote", (QuoteRequest request) =>
        {
            if (!Enum.TryParse<DeliveryOrderType>(request.OrderType, true, out var type))
                return Results.BadRequest(new { error = "OrderType must be Parcel or Grocery." });
            return Results.Ok(new { charge = DeliveryPricing.Calculate(type, request.DistanceKm, request.WeightKg) });
        });

        group.MapPost("/", async (
            CreateDeliveryOrderRequest request,
            ClaimsPrincipal principal,
            DeliveryDbContext db,
            ICacheService cache,
            IPublishEndpoint publisher) =>
        {
            if (!Enum.TryParse<DeliveryOrderType>(request.OrderType, true, out var type))
                return Results.BadRequest(new { error = "OrderType must be Parcel or Grocery." });

            var order = new DeliveryOrder
            {
                CustomerId = principal.GetUserId(),
                OrderType = type,
                Status = DeliveryStatus.AwaitingPayment,
                PickupAddress = request.PickupAddress,
                DropAddress = request.DropAddress,
                DistanceKm = request.DistanceKm,
                WeightKg = request.WeightKg,
                ItemsDescription = request.ItemsDescription,
                Charge = DeliveryPricing.Calculate(type, request.DistanceKm, request.WeightKg)
            };

            db.Orders.Add(order);
            db.TrackingEvents.Add(new TrackingEvent { OrderId = order.Id, Status = "Created" });
            await db.SaveChangesAsync();

            // Delivery-completion OTP lives in Redis, shown to the customer only.
            var otp = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
            await cache.SetStringAsync($"delivery:otp:{order.Id}", otp, OtpTtl);

            await publisher.Publish(new OrderCreatedEvent(
                order.Id, order.CustomerId, type.ToString(), order.Charge,
                order.PickupAddress, order.DropAddress, DateTime.UtcNow));

            return Results.Created($"/api/v1/deliveries/{order.Id}", order);
        });

        // Public grocery catalog — browsable without an account (cart gates at checkout).
        group.MapGet("/grocery-catalog", async (DeliveryDbContext db) =>
            Results.Ok(await db.GroceryItems.AsNoTracking()
                .Where(i => i.InStock)
                .OrderBy(i => i.Category).ThenBy(i => i.Name)
                .ToListAsync()))
            .AllowAnonymous();

        // Delivery partner earnings analytics: what was delivered and what they earn.
        group.MapGet("/partner/summary", async (ClaimsPrincipal principal, DeliveryDbContext db) =>
        {
            var partnerId = principal.GetUserId();
            var mine = db.Orders.AsNoTracking().Where(o => o.PartnerId == partnerId);

            var delivered = await mine.Where(o => o.Status == DeliveryStatus.Delivered).ToListAsync();
            var inFlight = await mine.CountAsync(o =>
                o.Status == DeliveryStatus.Assigned || o.Status == DeliveryStatus.PickedUp);
            var inFlightGross = await mine
                .Where(o => o.Status == DeliveryStatus.Assigned || o.Status == DeliveryStatus.PickedUp)
                .SumAsync(o => (decimal?)o.Charge) ?? 0m;

            var gross = delivered.Sum(o => o.Charge);
            const decimal partnerShare = 0.8m; // matches DeliveryCompletedEvent payout

            return Results.Ok(new
            {
                deliveredCount = delivered.Count,
                grossValue = Math.Round(gross, 2),
                earned = Math.Round(gross * partnerShare, 2),
                platformFee = Math.Round(gross * (1 - partnerShare), 2),
                inFlightCount = inFlight,
                pendingEarnings = Math.Round(inFlightGross * partnerShare, 2),
                shareRate = partnerShare,
                recent = delivered
                    .OrderByDescending(o => o.DeliveredAtUtc)
                    .Take(8)
                    .Select(o => new
                    {
                        o.Id,
                        o.DropAddress,
                        o.Charge,
                        earning = Math.Round(o.Charge * partnerShare, 2),
                        o.DeliveredAtUtc,
                        orderType = o.OrderType.ToString(),
                    }),
            });
        }).RequireAuthorization(policy => policy.RequireRole("DeliveryPartner"));

        group.MapGet("/mine", async (ClaimsPrincipal principal, DeliveryDbContext db) =>
        {
            var userId = principal.GetUserId();
            return Results.Ok(await db.Orders.AsNoTracking()
                .Where(o => o.CustomerId == userId || o.PartnerId == userId)
                .OrderByDescending(o => o.CreatedAtUtc)
                .Take(100).ToListAsync());
        });

        group.MapGet("/{orderId:guid}", async (Guid orderId, ClaimsPrincipal principal, DeliveryDbContext db) =>
        {
            var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId);
            if (order is null) return Results.NotFound();

            var userId = principal.GetUserId();
            if (order.CustomerId != userId && order.PartnerId != userId && principal.GetRole() != "Admin")
                return Results.Forbid();

            return Results.Ok(order);
        });

        group.MapGet("/{orderId:guid}/tracking", async (Guid orderId, DeliveryDbContext db) =>
            Results.Ok(await db.TrackingEvents.AsNoTracking()
                .Where(t => t.OrderId == orderId)
                .OrderBy(t => t.CreatedAtUtc)
                .ToListAsync()));

        // Customer fetches the OTP once a partner is assigned.
        group.MapGet("/{orderId:guid}/otp", async (Guid orderId, ClaimsPrincipal principal, DeliveryDbContext db, ICacheService cache) =>
        {
            var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId);
            if (order is null) return Results.NotFound();
            if (order.CustomerId != principal.GetUserId()) return Results.Forbid();
            if (order.PartnerId is null)
                return Results.Conflict(new { error = "OTP is available once a delivery partner is assigned." });

            var otp = await cache.GetStringAsync($"delivery:otp:{orderId}");
            return otp is null ? Results.NotFound() : Results.Ok(new { otp });
        });

        // Delivery partner marks pickup.
        group.MapPost("/{orderId:guid}/pickup", async (Guid orderId, ClaimsPrincipal principal, DeliveryDbContext db) =>
        {
            var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
            if (order is null) return Results.NotFound();
            if (order.PartnerId != principal.GetUserId()) return Results.Forbid();
            if (order.Status != DeliveryStatus.Assigned)
                return Results.Conflict(new { error = $"Cannot pick up an order in status {order.Status}." });

            order.Status = DeliveryStatus.PickedUp;
            order.UpdatedAtUtc = DateTime.UtcNow;
            db.TrackingEvents.Add(new TrackingEvent { OrderId = order.Id, Status = "PickedUp" });
            await db.SaveChangesAsync();
            return Results.Ok(order);
        }).RequireAuthorization(policy => policy.RequireRole("DeliveryPartner"));

        // Delivery partner submits the customer's OTP to complete the order.
        group.MapPost("/{orderId:guid}/complete", async (
            Guid orderId,
            CompleteDeliveryRequest request,
            ClaimsPrincipal principal,
            DeliveryDbContext db,
            ICacheService cache,
            IPublishEndpoint publisher) =>
        {
            var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
            if (order is null) return Results.NotFound();
            if (order.PartnerId != principal.GetUserId()) return Results.Forbid();
            if (order.Status != DeliveryStatus.PickedUp)
                return Results.Conflict(new { error = $"Cannot complete an order in status {order.Status}." });

            var expected = await cache.GetStringAsync($"delivery:otp:{orderId}");
            if (expected is null || expected != request.Otp)
                return Results.BadRequest(new { error = "Invalid or expired OTP." });

            order.Status = DeliveryStatus.Delivered;
            order.DeliveredAtUtc = DateTime.UtcNow;
            order.UpdatedAtUtc = DateTime.UtcNow;
            db.TrackingEvents.Add(new TrackingEvent { OrderId = order.Id, Status = "Delivered" });
            await db.SaveChangesAsync();
            await cache.RemoveAsync($"delivery:otp:{orderId}");

            var partnerEarning = Math.Round(order.Charge * 0.8m, 2); // 80% to the partner
            await publisher.Publish(new OtpVerifiedEvent(order.Id, order.PartnerId!.Value, DateTime.UtcNow));
            await publisher.Publish(new DeliveryCompletedEvent(
                order.Id, order.CustomerId, order.PartnerId!.Value, partnerEarning, DateTime.UtcNow));

            return Results.Ok(order);
        }).RequireAuthorization(policy => policy.RequireRole("DeliveryPartner"));
    }
}
