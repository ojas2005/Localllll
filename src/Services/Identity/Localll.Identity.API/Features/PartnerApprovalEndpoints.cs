using Localll.Contracts;
using Localll.Common.Auth;
using Localll.Identity.API.Data;
using Localll.Identity.API.Domain;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Localll.Identity.API.Features;

/// <summary>
/// Owner/admin review queue for partner-role applications. Accounts stay in
/// PendingApproval (credentials rejected at login) until approved here.
/// </summary>
public static class PartnerApprovalEndpoints
{
    public static void MapPartnerApprovalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/partner-applications")
            .WithTags("Partner Approvals")
            .RequireAuthorization(policy => policy.RequireRole(Roles.Admin));

        group.MapGet("/", async (IdentityDbContext db, string status = "PendingApproval") =>
        {
            var query = db.Users.AsNoTracking().Where(u => u.Role != Roles.Customer && u.Role != Roles.Admin);
            if (Enum.TryParse<ApprovalStatus>(status, true, out var parsed))
                query = query.Where(u => u.ApprovalStatus == parsed);

            var items = await query.OrderBy(u => u.CreatedAtUtc)
                .Select(u => new PartnerApplicationDto(
                    u.Id, u.FullName, u.Email, u.PhoneNumber, u.Role,
                    u.ApplicationNote, u.ApprovalStatus.ToString(), u.CreatedAtUtc))
                .Take(200)
                .ToListAsync();
            return Results.Ok(items);
        });

        group.MapPost("/{userId:guid}/review", async (
            Guid userId,
            ReviewApplicationRequest request,
            ClaimsPrincipal principal,
            IdentityDbContext db,
            IPublishEndpoint publisher,
            CancellationToken ct) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null) return Results.NotFound();
            if (user.ApprovalStatus != ApprovalStatus.PendingApproval)
                return Results.Conflict(new { error = $"Application is already {user.ApprovalStatus}." });

            user.ReviewedAtUtc = DateTime.UtcNow;
            user.ReviewedBy = principal.GetUserId();

            if (request.Approved)
            {
                user.ApprovalStatus = ApprovalStatus.Approved;
                await db.SaveChangesAsync(ct);

                // Only now does the rest of the platform learn about this account
                // (profile + wallet get created downstream).
                await publisher.Publish(new UserRegisteredEvent(
                    user.Id, user.Email, user.FullName, user.Role, DateTime.UtcNow), ct);
                await publisher.Publish(new NotificationRequestedEvent(
                    user.Id, "Email", "Application approved",
                    $"Congratulations! Your {user.Role} application was approved. You can now sign in to Localll.",
                    DateTime.UtcNow), ct);
            }
            else
            {
                user.ApprovalStatus = ApprovalStatus.Rejected;
                user.RejectionReason = request.RejectionReason;
                await db.SaveChangesAsync(ct);

                await publisher.Publish(new NotificationRequestedEvent(
                    user.Id, "Email", "Application update",
                    $"Your {user.Role} application was not approved." +
                    (request.RejectionReason is null ? "" : $" Reason: {request.RejectionReason}"),
                    DateTime.UtcNow), ct);
            }

            return Results.Ok(new { user.Id, status = user.ApprovalStatus.ToString() });
        });
    }
}
