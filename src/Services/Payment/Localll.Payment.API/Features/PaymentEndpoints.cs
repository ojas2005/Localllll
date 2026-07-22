using System.Security.Claims;
using Localll.Common.Auth;
using Localll.Contracts;
using Localll.Payment.API.Data;
using Localll.Payment.API.Domain;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Localll.Payment.API.Features;

public record InitiatePaymentRequest(Guid OrderId, decimal Amount, string Method);

public static class PaymentEndpoints
{
    private static readonly string[] SupportedMethods = ["Upi", "Card", "Wallet"];

    public static void MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/payments").WithTags("Payments").RequireAuthorization();

        // Idempotent: replaying the same Idempotency-Key returns the original payment.
        group.MapPost("/", async (
            InitiatePaymentRequest request,
            HttpRequest http,
            ClaimsPrincipal principal,
            PaymentDbContext db,
            IPublishEndpoint publisher) =>
        {
            var idempotencyKey = http.Headers["Idempotency-Key"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(idempotencyKey))
                return Results.BadRequest(new { error = "Idempotency-Key header is required." });
            if (!SupportedMethods.Contains(request.Method))
                return Results.BadRequest(new { error = $"Method must be one of: {string.Join(", ", SupportedMethods)}." });
            if (request.Amount <= 0)
                return Results.BadRequest(new { error = "Amount must be positive." });

            var existing = await db.Payments.AsNoTracking()
                .FirstOrDefaultAsync(p => p.IdempotencyKey == idempotencyKey);
            if (existing is not null) return Results.Ok(existing);

            var payment = new Domain.Payment
            {
                OrderId = request.OrderId,
                CustomerId = principal.GetUserId(),
                Amount = request.Amount,
                Method = request.Method,
                IdempotencyKey = idempotencyKey,
                // In production this is where the UPI/card provider is called;
                // the dev implementation settles instantly.
                Status = PaymentStatus.Completed,
                ProviderReference = $"MOCK-{Guid.NewGuid():N}",
                CompletedAtUtc = DateTime.UtcNow
            };

            db.Payments.Add(payment);
            await db.SaveChangesAsync();

            await publisher.Publish(new PaymentCompletedEvent(
                payment.Id, payment.OrderId, payment.CustomerId,
                payment.Amount, payment.Method, DateTime.UtcNow));

            return Results.Created($"/api/v1/payments/{payment.Id}", payment);
        });

        group.MapGet("/{paymentId:guid}", async (Guid paymentId, ClaimsPrincipal principal, PaymentDbContext db) =>
        {
            var payment = await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paymentId);
            if (payment is null) return Results.NotFound();
            if (payment.CustomerId != principal.GetUserId() && principal.GetRole() != "Admin")
                return Results.Forbid();
            return Results.Ok(payment);
        });

        group.MapGet("/order/{orderId:guid}", async (Guid orderId, PaymentDbContext db) =>
            Results.Ok(await db.Payments.AsNoTracking().Where(p => p.OrderId == orderId).ToListAsync()));

        group.MapPost("/{paymentId:guid}/refund", async (Guid paymentId, PaymentDbContext db) =>
        {
            var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId);
            if (payment is null) return Results.NotFound();
            if (payment.Status != PaymentStatus.Completed)
                return Results.Conflict(new { error = $"Cannot refund a payment in status {payment.Status}." });

            payment.Status = PaymentStatus.Refunded;
            payment.RefundedAtUtc = DateTime.UtcNow;
            payment.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(payment);
        }).RequireAuthorization(policy => policy.RequireRole("Admin"));
    }
}
