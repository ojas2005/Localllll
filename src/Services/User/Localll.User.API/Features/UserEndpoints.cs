using Localll.Common.Auth;
using Localll.Common.Caching;
using Localll.User.API.Data;
using Localll.User.API.Domain;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Localll.User.API.Features;

public record UpdateProfileRequest(string FullName, string? AvatarUrl, string? PreferredLanguage);
public record AddAddressRequest(string Label, string Line1, string? Line2, string City, string State, string PostalCode, double Latitude, double Longitude, bool IsDefault);
public record AddReviewRequest(Guid OrderId, string TargetType, Guid TargetId, int Rating, string? Comment);

public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/users").WithTags("Users").RequireAuthorization();

        group.MapGet("/me", async (ClaimsPrincipal principal, UserDbContext db, ICacheService cache) =>
        {
            var userId = principal.GetUserId();
            var cacheKey = $"profile:{userId}";

            var cached = await cache.GetAsync<CustomerProfile>(cacheKey);
            if (cached is not null) return Results.Ok(cached);

            var profile = await db.Profiles.Include(p => p.Addresses)
                .AsNoTracking().FirstOrDefaultAsync(p => p.Id == userId);
            if (profile is null) return Results.NotFound();

            await cache.SetAsync(cacheKey, profile, TimeSpan.FromMinutes(10));
            return Results.Ok(profile);
        });

        group.MapPut("/me", async (UpdateProfileRequest request, ClaimsPrincipal principal, UserDbContext db, ICacheService cache) =>
        {
            var userId = principal.GetUserId();
            var profile = await db.Profiles.FirstOrDefaultAsync(p => p.Id == userId);
            if (profile is null) return Results.NotFound();

            profile.FullName = request.FullName;
            profile.AvatarUrl = request.AvatarUrl;
            profile.PreferredLanguage = request.PreferredLanguage;
            profile.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await cache.RemoveAsync($"profile:{userId}");
            return Results.Ok(profile);
        });

        group.MapPost("/me/addresses", async (AddAddressRequest request, ClaimsPrincipal principal, UserDbContext db, ICacheService cache) =>
        {
            var userId = principal.GetUserId();
            if (!await db.Profiles.AnyAsync(p => p.Id == userId)) return Results.NotFound();

            if (request.IsDefault)
                await db.Addresses.Where(a => a.ProfileId == userId)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsDefault, false));

            var address = new Address
            {
                ProfileId = userId,
                Label = request.Label,
                Line1 = request.Line1,
                Line2 = request.Line2,
                City = request.City,
                State = request.State,
                PostalCode = request.PostalCode,
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                IsDefault = request.IsDefault
            };
            db.Addresses.Add(address);
            await db.SaveChangesAsync();
            await cache.RemoveAsync($"profile:{userId}");
            return Results.Created($"/api/v1/users/me/addresses/{address.Id}", address);
        });

        group.MapDelete("/me/addresses/{addressId:guid}", async (Guid addressId, ClaimsPrincipal principal, UserDbContext db, ICacheService cache) =>
        {
            var userId = principal.GetUserId();
            var deleted = await db.Addresses
                .Where(a => a.Id == addressId && a.ProfileId == userId)
                .ExecuteDeleteAsync();
            if (deleted == 0) return Results.NotFound();
            await cache.RemoveAsync($"profile:{userId}");
            return Results.NoContent();
        });

        group.MapPost("/me/reviews", async (AddReviewRequest request, ClaimsPrincipal principal, UserDbContext db) =>
        {
            if (request.Rating is < 1 or > 5)
                return Results.BadRequest(new { error = "Rating must be between 1 and 5." });

            var review = new Review
            {
                CustomerId = principal.GetUserId(),
                OrderId = request.OrderId,
                TargetType = request.TargetType,
                TargetId = request.TargetId,
                Rating = request.Rating,
                Comment = request.Comment
            };
            db.Reviews.Add(review);
            await db.SaveChangesAsync();
            return Results.Created($"/api/v1/users/reviews/{review.Id}", review);
        });

        // Public — average rating for a partner/pharmacy.
        app.MapGet("/api/v1/reviews/{targetType}/{targetId:guid}", async (string targetType, Guid targetId, UserDbContext db) =>
        {
            var query = db.Reviews.Where(r => r.TargetType == targetType && r.TargetId == targetId);
            var count = await query.CountAsync();
            var average = count == 0 ? 0 : await query.AverageAsync(r => r.Rating);
            return Results.Ok(new { targetType, targetId, count, average });
        }).WithTags("Users");
    }
}
