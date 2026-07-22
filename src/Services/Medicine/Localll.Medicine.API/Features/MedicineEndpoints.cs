using System.Security.Claims;
using Localll.Common.Auth;
using Localll.Common.Caching;
using Localll.Contracts;
using Localll.Medicine.API.Data;
using Localll.Medicine.API.Domain;
using Localll.SharedKernel;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Localll.Medicine.API.Features;

public record OrderItemRequest(Guid MedicineId, int Quantity);
public record CreateMedicineOrderRequest(string DeliveryAddress, string? PrescriptionUrl, List<OrderItemRequest> Items);
public record ApproveOrderRequest(bool Approved, string? RejectionReason);

public static class MedicineEndpoints
{
    public static void MapMedicineEndpoints(this IEndpointRouteBuilder app)
    {
        var medicines = app.MapGroup("/api/v1/medicines").WithTags("Medicines");

        // Public catalog search with Redis-backed caching of popular queries.
        medicines.MapGet("/search", async (string query, MedicineDbContext db, ICacheService cache, int page = 1, int pageSize = 20) =>
        {
            var cacheKey = $"medsearch:{query.ToLowerInvariant()}:{page}:{pageSize}";
            var cached = await cache.GetAsync<PagedResult<Domain.Medicine>>(cacheKey);
            if (cached is not null) return Results.Ok(cached);

            var source = db.Medicines.AsNoTracking()
                .Where(m => EF.Functions.ILike(m.Name, $"%{query}%")
                            || (m.GenericName != null && EF.Functions.ILike(m.GenericName, $"%{query}%")));

            var total = await source.LongCountAsync();
            var items = await source.OrderBy(m => m.Name)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            var result = new PagedResult<Domain.Medicine>(items, page, pageSize, total);
            await cache.SetAsync(cacheKey, result, TimeSpan.FromMinutes(10));
            return Results.Ok(result);
        });

        medicines.MapGet("/{medicineId:guid}", async (Guid medicineId, MedicineDbContext db) =>
        {
            var medicine = await db.Medicines.AsNoTracking().FirstOrDefaultAsync(m => m.Id == medicineId);
            return medicine is null ? Results.NotFound() : Results.Ok(medicine);
        });

        var orders = app.MapGroup("/api/v1/medicine-orders").WithTags("Medicine Orders").RequireAuthorization();

        orders.MapPost("/", async (
            CreateMedicineOrderRequest request,
            ClaimsPrincipal principal,
            MedicineDbContext db,
            IPublishEndpoint publisher) =>
        {
            if (request.Items.Count == 0)
                return Results.BadRequest(new { error = "Order must contain at least one item." });

            var medicineIds = request.Items.Select(i => i.MedicineId).ToList();
            var catalog = await db.Medicines.Where(m => medicineIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id);
            if (catalog.Count != medicineIds.Distinct().Count())
                return Results.BadRequest(new { error = "One or more medicines were not found." });

            var needsPrescription = catalog.Values.Any(m => m.RequiresPrescription);
            if (needsPrescription && string.IsNullOrWhiteSpace(request.PrescriptionUrl))
                return Results.BadRequest(new { error = "A prescription is required for one or more items." });

            var order = new MedicineOrder
            {
                CustomerId = principal.GetUserId(),
                DeliveryAddress = request.DeliveryAddress,
                PrescriptionUrl = request.PrescriptionUrl,
                Status = needsPrescription ? MedicineOrderStatus.PendingApproval : MedicineOrderStatus.AwaitingPayment
            };

            foreach (var item in request.Items)
            {
                var medicine = catalog[item.MedicineId];
                order.Items.Add(new MedicineOrderItem
                {
                    OrderId = order.Id,
                    MedicineId = medicine.Id,
                    MedicineName = medicine.Name,
                    Quantity = item.Quantity,
                    UnitPrice = medicine.Price
                });
            }
            order.TotalAmount = order.Items.Sum(i => i.UnitPrice * i.Quantity);

            db.Orders.Add(order);
            await db.SaveChangesAsync();

            // Orders that skip approval are immediately visible to payment/partners.
            if (order.Status == MedicineOrderStatus.AwaitingPayment)
                await publisher.Publish(new OrderCreatedEvent(
                    order.Id, order.CustomerId, "Medicine", order.TotalAmount,
                    "Pharmacy", order.DeliveryAddress, DateTime.UtcNow));

            return Results.Created($"/api/v1/medicine-orders/{order.Id}", order);
        });

        orders.MapGet("/mine", async (ClaimsPrincipal principal, MedicineDbContext db) =>
        {
            var userId = principal.GetUserId();
            return Results.Ok(await db.Orders.Include(o => o.Items).AsNoTracking()
                .Where(o => o.CustomerId == userId)
                .OrderByDescending(o => o.CreatedAtUtc)
                .Take(100).ToListAsync());
        });

        orders.MapGet("/{orderId:guid}", async (Guid orderId, ClaimsPrincipal principal, MedicineDbContext db) =>
        {
            var order = await db.Orders.Include(o => o.Items)
                .AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId);
            if (order is null) return Results.NotFound();

            var role = principal.GetRole();
            if (order.CustomerId != principal.GetUserId() && role is not ("Admin" or "PharmacyPartner"))
                return Results.Forbid();
            return Results.Ok(order);
        });

        // Pharmacy sales analytics: what they've sold and what they'll be paid.
        orders.MapGet("/pharmacy/summary", async (ClaimsPrincipal principal, MedicineDbContext db) =>
        {
            var pharmacyId = principal.GetUserId();
            // "Sold" = orders this pharmacy approved that the customer has paid
            // (or that progressed beyond payment).
            var soldStatuses = new[]
            {
                MedicineOrderStatus.Paid, MedicineOrderStatus.Dispatched, MedicineOrderStatus.Delivered,
            };
            var sold = await db.Orders.Include(o => o.Items).AsNoTracking()
                .Where(o => o.PharmacyId == pharmacyId && soldStatuses.Contains(o.Status))
                .ToListAsync();
            var awaitingPayment = await db.Orders.AsNoTracking()
                .CountAsync(o => o.PharmacyId == pharmacyId && o.Status == MedicineOrderStatus.AwaitingPayment);

            var gross = sold.Sum(o => o.TotalAmount);
            const decimal commission = 0.10m; // platform keeps 10% of medicine sales

            var topItems = sold.SelectMany(o => o.Items)
                .GroupBy(i => i.MedicineName)
                .Select(g => new
                {
                    name = g.Key,
                    unitsSold = g.Sum(i => i.Quantity),
                    revenue = Math.Round(g.Sum(i => i.UnitPrice * i.Quantity), 2),
                })
                .OrderByDescending(x => x.revenue)
                .Take(8)
                .ToList();

            return Results.Ok(new
            {
                ordersSold = sold.Count,
                itemsSold = sold.SelectMany(o => o.Items).Sum(i => i.Quantity),
                grossSales = Math.Round(gross, 2),
                commission = Math.Round(gross * commission, 2),
                payout = Math.Round(gross * (1 - commission), 2),
                commissionRate = commission,
                awaitingPayment,
                topItems,
                recent = sold.OrderByDescending(o => o.UpdatedAtUtc ?? o.CreatedAtUtc)
                    .Take(8)
                    .Select(o => new
                    {
                        o.Id,
                        o.TotalAmount,
                        payout = Math.Round(o.TotalAmount * (1 - commission), 2),
                        status = o.Status.ToString(),
                        items = o.Items.Count,
                        soldAtUtc = o.UpdatedAtUtc ?? o.CreatedAtUtc,
                    }),
            });
        }).RequireAuthorization(policy => policy.RequireRole("PharmacyPartner", "Admin"));

        orders.MapGet("/pending-approval", async (MedicineDbContext db) =>
            Results.Ok(await db.Orders.Include(o => o.Items).AsNoTracking()
                .Where(o => o.Status == MedicineOrderStatus.PendingApproval)
                .OrderBy(o => o.CreatedAtUtc)
                .Take(100).ToListAsync()))
            .RequireAuthorization(policy => policy.RequireRole("PharmacyPartner", "Admin"));

        // Pharmacist approves or rejects a prescription order.
        orders.MapPost("/{orderId:guid}/review", async (
            Guid orderId,
            ApproveOrderRequest request,
            ClaimsPrincipal principal,
            MedicineDbContext db,
            IPublishEndpoint publisher) =>
        {
            var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
            if (order is null) return Results.NotFound();
            if (order.Status != MedicineOrderStatus.PendingApproval)
                return Results.Conflict(new { error = $"Order is in status {order.Status}, not PendingApproval." });

            var pharmacyId = principal.GetUserId();
            if (request.Approved)
            {
                order.Status = MedicineOrderStatus.AwaitingPayment;
                order.PharmacyId = pharmacyId;
                order.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();

                await publisher.Publish(new MedicineApprovedEvent(
                    order.Id, pharmacyId, order.CustomerId, order.TotalAmount, DateTime.UtcNow));
                await publisher.Publish(new OrderCreatedEvent(
                    order.Id, order.CustomerId, "Medicine", order.TotalAmount,
                    "Pharmacy", order.DeliveryAddress, DateTime.UtcNow));
            }
            else
            {
                order.Status = MedicineOrderStatus.Rejected;
                order.RejectionReason = request.RejectionReason ?? "Rejected by pharmacist.";
                order.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }

            return Results.Ok(order);
        }).RequireAuthorization(policy => policy.RequireRole("PharmacyPartner", "Admin"));
    }
}
