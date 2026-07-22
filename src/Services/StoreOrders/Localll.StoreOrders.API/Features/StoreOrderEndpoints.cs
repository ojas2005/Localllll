using System.Security.Claims;
using Localll.Common.Auth;
using Localll.Contracts;
using Localll.StoreOrders.API.Data;
using Localll.StoreOrders.API.Domain;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Localll.StoreOrders.API.Features;

public static class StoreOrderEndpoints
{
    public static void MapStoreOrderEndpoints(this IEndpointRouteBuilder app)
    {
        // ---------- Public catalog ----------
        var stores = app.MapGroup("/api/v1/store").WithTags("Store Catalog");

        stores.MapGet("/stores", async (StoreOrdersDbContext db) =>
            Results.Ok(await db.Stores.AsNoTracking().OrderBy(s => s.Name)
                .Select(s => new { s.Id, s.Name, s.Address, s.City }).ToListAsync()));

        stores.MapGet("/stores/{storeId:guid}/products", async (Guid storeId, StoreOrdersDbContext db) =>
            Results.Ok(await db.Products.AsNoTracking()
                .Where(p => p.StoreId == storeId && p.InStock)
                .OrderBy(p => p.Category).ThenBy(p => p.Name).ToListAsync()));

        MapCustomerEndpoints(app);
        MapAdminEndpoints(app);
        MapDeliveryEndpoints(app);
        MapStoreOwnerEndpoints(app);
    }

    // ==================== CUSTOMER ====================
    private static void MapCustomerEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/store/orders").WithTags("Store Orders").RequireAuthorization();

        // Create an order. UPI → PendingPaymentVerification (after screenshot); COD → PendingAdminApproval.
        group.MapPost("/", async (CreateOrderRequest request, ClaimsPrincipal principal, StoreOrdersDbContext db) =>
        {
            if (request.Items.Count == 0)
                return Results.BadRequest(new { error = "Order must contain at least one item." });
            if (!Enum.TryParse<PaymentMethod>(request.PaymentMethod, true, out var method))
                return Results.BadRequest(new { error = "PaymentMethod must be Upi or Cod." });

            var store = await db.Stores.FirstOrDefaultAsync(s => s.Id == request.StoreId);
            if (store is null) return Results.NotFound(new { error = "Store not found." });

            var productIds = request.Items.Select(i => i.ProductId).ToList();
            var catalog = await db.Products
                .Where(p => p.StoreId == store.Id && productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id);
            if (catalog.Count != productIds.Distinct().Count())
                return Results.BadRequest(new { error = "One or more products are not in this store." });

            var order = new StoreOrder
            {
                CustomerId = principal.GetUserId(),
                CustomerName = request.CustomerName,
                CustomerPhone = request.CustomerPhone,
                DeliveryAddress = request.DeliveryAddress,
                StoreId = store.Id,
                StoreName = store.Name,
                StoreAddress = store.Address,
                PaymentMethod = method,
                Status = StoreOrderStatus.Created,
                DeliveryCharge = Settlement.DeliveryCharge,
            };
            foreach (var line in request.Items)
            {
                var product = catalog[line.ProductId];
                order.Items.Add(new StoreOrderItem
                {
                    OrderId = order.Id, ProductId = product.Id, ProductName = product.Name,
                    Quantity = line.Quantity, UnitPrice = product.Price,
                });
            }
            order.ItemsTotal = order.Items.Sum(i => i.LineTotal);
            order.ServiceCharge = Settlement.ServiceChargeFor(order.ItemsTotal);
            order.GrandTotal = order.ItemsTotal + order.ServiceCharge + order.DeliveryCharge;

            // COD needs no proof — straight to admin approval. UPI stays Created until the
            // screenshot is attached (place-order gate).
            if (method == PaymentMethod.Cod)
                order.Status = StoreOrderStatus.PendingAdminApproval;

            db.Orders.Add(order);
            await db.SaveChangesAsync();

            if (method == PaymentMethod.Upi)
            {
                var deepLink = $"upi://pay?pa={Uri.EscapeDataString(store.UpiId)}&pn={Uri.EscapeDataString(store.Name)}" +
                               $"&am={order.GrandTotal:0.00}&cu=INR&tn={Uri.EscapeDataString("Localll order " + order.Id.ToString()[..8])}";
                return Results.Created($"/api/v1/store/orders/{order.Id}",
                    new UpiPaymentInfo(order.Id, store.UpiId, store.Name, order.GrandTotal, deepLink));
            }
            return Results.Created($"/api/v1/store/orders/{order.Id}", ToDto(order));
        });

        // Attach the UPI payment screenshot and move to PendingPaymentVerification.
        // This is the "Place Order" action for UPI — only reachable with a screenshot.
        group.MapPost("/{orderId:guid}/screenshot", async (
            Guid orderId, AttachScreenshotRequest request, ClaimsPrincipal principal, StoreOrdersDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.ScreenshotUrl))
                return Results.BadRequest(new { error = "A payment screenshot is required to place a UPI order." });

            var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
            if (order is null) return Results.NotFound();
            if (order.CustomerId != principal.GetUserId()) return Results.Forbid();
            if (order.PaymentMethod != PaymentMethod.Upi)
                return Results.Conflict(new { error = "Screenshots only apply to UPI orders." });
            if (order.Status is not (StoreOrderStatus.Created or StoreOrderStatus.PendingPaymentVerification))
                return Results.Conflict(new { error = $"Order is already {order.Status}." });

            order.PaymentScreenshotUrl = request.ScreenshotUrl;
            order.Status = StoreOrderStatus.PendingPaymentVerification;
            order.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(order));
        });

        group.MapGet("/mine", async (ClaimsPrincipal principal, StoreOrdersDbContext db) =>
        {
            var userId = principal.GetUserId();
            var orders = await db.Orders.Include(o => o.Items).AsNoTracking()
                .Where(o => o.CustomerId == userId)
                .OrderByDescending(o => o.CreatedAtUtc).Take(100).ToListAsync();
            return Results.Ok(orders.Select(ToDto));
        });

        group.MapGet("/{orderId:guid}", async (Guid orderId, ClaimsPrincipal principal, StoreOrdersDbContext db) =>
        {
            var order = await db.Orders.Include(o => o.Items).AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId);
            if (order is null) return Results.NotFound();
            var role = principal.GetRole();
            if (order.CustomerId != principal.GetUserId() && order.DeliveryPartnerId != principal.GetUserId()
                && role is not ("Admin" or "PharmacyPartner"))
                return Results.Forbid();
            return Results.Ok(ToDto(order));
        });
    }

    // ==================== ADMIN (payment verification) ====================
    private static void MapAdminEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/store/admin/orders").WithTags("Store Admin")
            .RequireAuthorization(policy => policy.RequireRole("Admin"));

        // Everything the admin needs to verify: customer, address, items, total, method, screenshot, time.
        group.MapGet("/pending", async (StoreOrdersDbContext db) =>
        {
            var pending = new[] { StoreOrderStatus.PendingPaymentVerification, StoreOrderStatus.PendingAdminApproval };
            var orders = await db.Orders.Include(o => o.Items).AsNoTracking()
                .Where(o => pending.Contains(o.Status))
                .OrderBy(o => o.CreatedAtUtc).Take(200).ToListAsync();
            return Results.Ok(orders.Select(ToDto));
        });

        group.MapPost("/{orderId:guid}/review", async (
            Guid orderId, ReviewPaymentRequest request, StoreOrdersDbContext db, IPublishEndpoint publisher) =>
        {
            var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
            if (order is null) return Results.NotFound();
            if (order.Status is not (StoreOrderStatus.PendingPaymentVerification or StoreOrderStatus.PendingAdminApproval))
                return Results.Conflict(new { error = $"Order is {order.Status}, not awaiting review." });

            order.UpdatedAtUtc = DateTime.UtcNow;
            if (request.Approved)
            {
                order.Status = StoreOrderStatus.WaitingForDeliveryPartner; // approved → visible to partners
                order.ApprovedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();
                await publisher.Publish(new NotificationRequestedEvent(
                    order.CustomerId, "Push", "Payment approved",
                    $"Your order from {order.StoreName} is approved and being assigned to a delivery partner.", DateTime.UtcNow));
            }
            else
            {
                order.Status = StoreOrderStatus.PaymentRejected;
                order.RejectionReason = request.RejectionReason;
                await db.SaveChangesAsync();
                await publisher.Publish(new NotificationRequestedEvent(
                    order.CustomerId, "Push", "Payment rejected",
                    $"Your payment for the {order.StoreName} order was rejected." +
                    (request.RejectionReason is null ? "" : $" Reason: {request.RejectionReason}"), DateTime.UtcNow));
            }
            return Results.Ok(ToDto(order));
        });

        // Admin oversight of every store's settlement wallet.
        group.MapGet("/store-wallets", async (StoreOrdersDbContext db) =>
        {
            var wallets = await db.Wallets.AsNoTracking().ToListAsync();
            var stores = await db.Stores.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name);
            return Results.Ok(wallets.Select(w => new
            {
                w.StoreId,
                storeName = stores.GetValueOrDefault(w.StoreId, "Store"),
                w.Balance,
            }));
        });
    }

    // ==================== DELIVERY PARTNER ====================
    private static void MapDeliveryEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/store/delivery").WithTags("Store Delivery")
            .RequireAuthorization(policy => policy.RequireRole("DeliveryPartner"));

        // Available (approved, unassigned) orders — first-come-first-served feed.
        group.MapGet("/available", async (StoreOrdersDbContext db) =>
        {
            var orders = await db.Orders.Include(o => o.Items).AsNoTracking()
                .Where(o => o.Status == StoreOrderStatus.WaitingForDeliveryPartner && o.DeliveryPartnerId == null)
                .OrderBy(o => o.ApprovedAtUtc).Take(100).ToListAsync();
            return Results.Ok(orders.Select(o => new
            {
                o.Id, o.StoreName, pickup = o.StoreAddress, o.DeliveryAddress,
                items = o.Items.Count, o.GrandTotal,
                estimatedEarning = o.DeliveryCharge,
                o.ApprovedAtUtc,
            }));
        });

        // Atomic accept: a single conditional UPDATE claims the order. Whoever's write
        // affects a row wins; everyone else gets 0 rows (already taken).
        group.MapPost("/{orderId:guid}/accept", async (
            Guid orderId, ClaimsPrincipal principal, StoreOrdersDbContext db, IPublishEndpoint publisher) =>
        {
            var partnerId = principal.GetUserId();
            var partnerName = principal.FindFirstValue(ClaimTypes.Name) ?? "Delivery Partner";

            var claimed = await db.Orders
                .Where(o => o.Id == orderId
                            && o.Status == StoreOrderStatus.WaitingForDeliveryPartner
                            && o.DeliveryPartnerId == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.DeliveryPartnerId, partnerId)
                    .SetProperty(o => o.DeliveryPartnerName, partnerName)
                    .SetProperty(o => o.Status, StoreOrderStatus.Assigned)
                    .SetProperty(o => o.AssignedAtUtc, DateTime.UtcNow)
                    .SetProperty(o => o.UpdatedAtUtc, DateTime.UtcNow));

            if (claimed == 0)
                return Results.Conflict(new { error = "This order was just taken by another partner." });

            var order = await db.Orders.Include(o => o.Items).AsNoTracking().FirstAsync(o => o.Id == orderId);
            await publisher.Publish(new NotificationRequestedEvent(
                order.CustomerId, "Push", "Partner assigned",
                $"{partnerName} is handling your {order.StoreName} order.", DateTime.UtcNow));
            return Results.Ok(ToDto(order));
        });

        group.MapGet("/mine", async (ClaimsPrincipal principal, StoreOrdersDbContext db) =>
        {
            var partnerId = principal.GetUserId();
            var orders = await db.Orders.Include(o => o.Items).AsNoTracking()
                .Where(o => o.DeliveryPartnerId == partnerId)
                .OrderByDescending(o => o.AssignedAtUtc).Take(100).ToListAsync();
            return Results.Ok(orders.Select(ToDto));
        });

        // Pickup: Assigned → CollectedFromStore
        group.MapPost("/{orderId:guid}/collected", (Guid orderId, ClaimsPrincipal principal, StoreOrdersDbContext db) =>
            Advance(db, orderId, principal.GetUserId(), StoreOrderStatus.Assigned, StoreOrderStatus.CollectedFromStore,
                o => o.CollectedAtUtc = DateTime.UtcNow));

        // Out for delivery: CollectedFromStore → OutForDelivery
        group.MapPost("/{orderId:guid}/out-for-delivery", (Guid orderId, ClaimsPrincipal principal, StoreOrdersDbContext db) =>
            Advance(db, orderId, principal.GetUserId(), StoreOrderStatus.CollectedFromStore, StoreOrderStatus.OutForDelivery, null));

        // Delivered → settle the store wallet in the same transaction → StoreCredited.
        group.MapPost("/{orderId:guid}/delivered", async (
            Guid orderId, ClaimsPrincipal principal, StoreOrdersDbContext db, IPublishEndpoint publisher) =>
        {
            var partnerId = principal.GetUserId();
            await using var tx = await db.Database.BeginTransactionAsync();

            var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
            if (order is null) return Results.NotFound();
            if (order.DeliveryPartnerId != partnerId) return Results.Forbid();
            if (order.Status != StoreOrderStatus.OutForDelivery)
                return Results.Conflict(new { error = $"Cannot deliver an order in status {order.Status}." });

            order.Status = StoreOrderStatus.Delivered;
            order.DeliveredAtUtc = DateTime.UtcNow;

            // Settlement — only now, after successful delivery.
            order.PlatformCommission = Settlement.Commission(order.ItemsTotal);
            order.StorePayout = Settlement.StorePayout(order.ItemsTotal);
            order.PartnerEarning = order.DeliveryCharge;

            var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.StoreId == order.StoreId);
            var isNewWallet = wallet is null;
            if (wallet is null)
            {
                wallet = new StoreWallet { StoreId = order.StoreId };
                db.Wallets.Add(wallet);
            }
            var entry = wallet.Credit(order.StorePayout, $"Order {order.Id.ToString()[..8]} settlement", order.Id);
            // Explicit Add: a ledger entry attached to an already-tracked wallet has a
            // pre-set Guid key, so EF would otherwise treat it as an UPDATE (0 rows →
            // concurrency exception). New-wallet graphs are inserted whole, so skip then.
            if (!isNewWallet) db.WalletEntries.Add(entry);

            order.Status = StoreOrderStatus.StoreCredited;
            order.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await tx.CommitAsync();

            await publisher.Publish(new NotificationRequestedEvent(
                order.CustomerId, "Push", "Order delivered",
                $"Your {order.StoreName} order was delivered. Thanks for using Localll!", DateTime.UtcNow));
            return Results.Ok(ToDto(order));
        });
    }

    // ==================== STORE OWNER (settlement wallet) ====================
    private static void MapStoreOwnerEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/store/owner").WithTags("Store Owner")
            .RequireAuthorization(policy => policy.RequireRole("PharmacyPartner", "Admin"));

        // A store owner can claim an unowned store so its wallet is theirs (dev convenience).
        group.MapPost("/claim/{storeId:guid}", async (Guid storeId, ClaimsPrincipal principal, StoreOrdersDbContext db) =>
        {
            var store = await db.Stores.FirstOrDefaultAsync(s => s.Id == storeId);
            if (store is null) return Results.NotFound();
            if (store.OwnerUserId is not null && store.OwnerUserId != principal.GetUserId())
                return Results.Conflict(new { error = "Store already has an owner." });
            store.OwnerUserId = principal.GetUserId();
            await db.SaveChangesAsync();
            return Results.Ok(new { store.Id, store.Name });
        });

        group.MapGet("/settlement", async (ClaimsPrincipal principal, StoreOrdersDbContext db) =>
        {
            var ownerId = principal.GetUserId();
            var storeIds = await db.Stores.AsNoTracking()
                .Where(s => s.OwnerUserId == ownerId).Select(s => s.Id).ToListAsync();
            if (storeIds.Count == 0)
                return Results.Ok(new { balance = 0m, pending = 0m, credited = 0, entries = Array.Empty<object>() });

            var wallets = await db.Wallets.Include(w => w.Entries).AsNoTracking()
                .Where(w => storeIds.Contains(w.StoreId)).ToListAsync();
            var balance = wallets.Sum(w => w.Balance);
            var entries = wallets.SelectMany(w => w.Entries)
                .OrderByDescending(e => e.CreatedAtUtc).Take(30)
                .Select(e => new { e.Type, e.Amount, e.BalanceAfter, e.Reason, e.CreatedAtUtc });

            // Money still owed for orders delivered but (impossible here) or in-flight before delivery.
            var pending = await db.Orders.AsNoTracking()
                .Where(o => storeIds.Contains(o.StoreId) &&
                       o.Status != StoreOrderStatus.StoreCredited &&
                       o.Status != StoreOrderStatus.PaymentRejected &&
                       o.Status != StoreOrderStatus.Cancelled)
                .SumAsync(o => (decimal?)o.ItemsTotal) ?? 0m;

            return Results.Ok(new
            {
                balance,
                pending = Math.Round(pending * (1 - Settlement.CommissionRate), 2),
                credited = wallets.SelectMany(w => w.Entries).Count(e => e.Type == StoreWalletEntryType.Credit),
                entries,
            });
        });

        group.MapPost("/withdraw", async (WithdrawRequest request, ClaimsPrincipal principal, StoreOrdersDbContext db) =>
        {
            var ownerId = principal.GetUserId();
            var storeId = await db.Stores.Where(s => s.OwnerUserId == ownerId).Select(s => s.Id).FirstOrDefaultAsync();
            if (storeId == Guid.Empty) return Results.NotFound(new { error = "No store linked to this account." });

            var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.StoreId == storeId);
            if (wallet is null) return Results.Conflict(new { error = "No settlement balance yet." });

            var result = wallet.Debit(request.Amount, "Withdrawal to bank");
            if (result.IsFailure) return Results.Conflict(new { error = result.Error.Message });
            db.WalletEntries.Add(result.Value);
            await db.SaveChangesAsync();
            return Results.Ok(new { wallet.Balance });
        });
    }

    // ---------- helpers ----------
    private static async Task<IResult> Advance(
        StoreOrdersDbContext db, Guid orderId, Guid partnerId,
        StoreOrderStatus from, StoreOrderStatus to, Action<StoreOrder>? stamp)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order is null) return Results.NotFound();
        if (order.DeliveryPartnerId != partnerId) return Results.Forbid();
        if (order.Status != from)
            return Results.Conflict(new { error = $"Cannot move to {to} from {order.Status}." });
        order.Status = to;
        stamp?.Invoke(order);
        order.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Results.Ok(ToDto(order));
    }

    private static object ToDto(StoreOrder o) => new
    {
        o.Id,
        o.CustomerName, o.CustomerPhone, o.DeliveryAddress,
        o.StoreId, o.StoreName, o.StoreAddress,
        status = o.Status.ToString(),
        paymentMethod = o.PaymentMethod.ToString(),
        o.PaymentScreenshotUrl, o.RejectionReason,
        o.ItemsTotal, o.ServiceCharge, o.DeliveryCharge, o.GrandTotal,
        o.PlatformCommission, o.StorePayout,
        o.DeliveryPartnerId, o.DeliveryPartnerName, o.PartnerEarning,
        o.CreatedAtUtc, o.ApprovedAtUtc, o.AssignedAtUtc, o.CollectedAtUtc, o.DeliveredAtUtc,
        items = o.Items.Select(i => new { i.ProductName, i.Quantity, i.UnitPrice, lineTotal = i.LineTotal }),
    };
}
