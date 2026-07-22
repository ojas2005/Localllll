using System.Security.Cryptography;
using FluentValidation;
using Localll.Common.Caching;
using Localll.Contracts;
using Localll.Identity.API.Data;
using Localll.Identity.API.Domain;
using Localll.Identity.API.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Localll.Identity.API.Features;

public static class AuthEndpoints
{
    private const int MaxFailedLogins = 5;
    private static readonly TimeSpan OtpTtl = TimeSpan.FromMinutes(5);

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/register", RegisterAsync);
        group.MapPost("/login", LoginAsync);
        group.MapPost("/google", GoogleLoginAsync);
        group.MapPost("/refresh", RefreshAsync);
        group.MapPost("/otp/request", RequestOtpAsync);
        group.MapPost("/otp/verify", VerifyOtpAsync);
        group.MapPost("/forgot-password", ForgotPasswordAsync);
        group.MapPost("/reset-password", ResetPasswordAsync);
        group.MapPost("/logout", LogoutAsync).RequireAuthorization();
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        IValidator<RegisterRequest> validator,
        IdentityDbContext db,
        TokenService tokens,
        IPublishEndpoint publisher,
        CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

        // Admin can never be self-registered; unknown roles fall back to Customer.
        var role = Roles.SelfRegisterable.Contains(request.Role) ? request.Role! : Roles.Customer;
        var emailTaken = await db.Users.AnyAsync(u => u.Email == request.Email || u.PhoneNumber == request.PhoneNumber, ct);
        if (emailTaken)
            return Results.Conflict(new { error = "An account with this email or phone number already exists." });

        var needsApproval = Roles.RequiresApproval(role);
        var user = new ApplicationUser
        {
            Email = request.Email.ToLowerInvariant(),
            PhoneNumber = request.PhoneNumber,
            FullName = request.FullName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = role,
            ApprovalStatus = needsApproval ? ApprovalStatus.PendingApproval : ApprovalStatus.Approved,
            ApplicationNote = needsApproval ? request.ApplicationNote : null
        };

        if (needsApproval)
        {
            // Partner-role application: store the account but issue NO tokens.
            // Credentials only start working after the owner approves.
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);

            await publisher.Publish(new NotificationRequestedEvent(
                Guid.Empty, "Email", "New partner application",
                $"{user.FullName} ({user.Email}) applied for the {role} role. Review it in Partner Approvals.",
                DateTime.UtcNow), ct);

            return Results.Accepted(value: new PendingApplicationResponse(
                "PendingApproval", role,
                "Your application has been sent to the Localll team. You'll be able to sign in once it is approved."));
        }

        var refreshToken = tokens.CreateRefreshToken(user.Id);
        user.RefreshTokens.Add(refreshToken);

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        await publisher.Publish(new UserRegisteredEvent(user.Id, user.Email, user.FullName, user.Role, DateTime.UtcNow), ct);

        var (accessToken, expiresAt) = tokens.CreateAccessToken(user);
        return Results.Created($"/api/v1/users/{user.Id}",
            new AuthResponse(user.Id, user.Email, user.FullName, user.Role, accessToken, expiresAt, refreshToken.Token));
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        IValidator<LoginRequest> validator,
        IdentityDbContext db,
        TokenService tokens,
        CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

        var user = await db.Users.Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.Email == request.Email.ToLowerInvariant(), ct);

        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            if (user is not null)
            {
                user.FailedLoginAttempts++;
                if (user.FailedLoginAttempts >= MaxFailedLogins) user.IsLocked = true;
                await db.SaveChangesAsync(ct);
            }
            return Results.Unauthorized();
        }

        if (user.IsLocked)
            return Results.Problem(statusCode: 423, title: "Account locked after too many failed attempts.");

        // Partner applications must be approved by the owner before the
        // credentials work — correct password or not.
        if (user.ApprovalStatus == ApprovalStatus.PendingApproval)
            return Results.Problem(statusCode: 403,
                title: "Application pending approval",
                detail: $"Your {user.Role} application is still being reviewed by the Localll team. You'll be notified once it's approved.");
        if (user.ApprovalStatus == ApprovalStatus.Rejected)
            return Results.Problem(statusCode: 403,
                title: "Application rejected",
                detail: user.RejectionReason ?? "Your partner application was not approved. Contact support for details.");

        user.FailedLoginAttempts = 0;
        user.LastLoginAtUtc = DateTime.UtcNow;

        // Explicit Add: entities appended to a tracked parent's navigation are
        // otherwise assumed to already exist (pre-set Guid key) and get UPDATEd.
        var refreshToken = tokens.CreateRefreshToken(user.Id);
        db.RefreshTokens.Add(refreshToken);
        await db.SaveChangesAsync(ct);

        var (accessToken, expiresAt) = tokens.CreateAccessToken(user);
        return Results.Ok(new AuthResponse(user.Id, user.Email, user.FullName, user.Role, accessToken, expiresAt, refreshToken.Token));
    }

    private static async Task<IResult> GoogleLoginAsync(
        GoogleLoginRequest request,
        IdentityDbContext db,
        TokenService tokens,
        IPublishEndpoint publisher,
        IConfiguration config,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var clientId = config["Google:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
            return Results.Problem(statusCode: 501, title: "Google sign-in is not configured",
                detail: "Set Google:ClientId in the Identity service configuration.");

        Google.Apis.Auth.GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await Google.Apis.Auth.GoogleJsonWebSignature.ValidateAsync(request.IdToken,
                new Google.Apis.Auth.GoogleJsonWebSignature.ValidationSettings { Audience = [clientId] });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Google id_token validation failed");
            return Results.Unauthorized();
        }

        var user = await db.Users.FirstOrDefaultAsync(
            u => u.GoogleSubject == payload.Subject || u.Email == payload.Email.ToLower(), ct);

        if (user is null)
        {
            // First Google sign-in: create a Customer account (partner roles
            // must go through the application form + owner approval).
            user = new ApplicationUser
            {
                Email = payload.Email.ToLowerInvariant(),
                PhoneNumber = $"g-{payload.Subject}",   // placeholder until the user adds one
                FullName = payload.Name ?? payload.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N")),
                Role = Roles.Customer,
                ApprovalStatus = ApprovalStatus.Approved,
                GoogleSubject = payload.Subject,
                EmailVerified = payload.EmailVerified
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
            await publisher.Publish(new UserRegisteredEvent(user.Id, user.Email, user.FullName, user.Role, DateTime.UtcNow), ct);
        }
        else if (user.GoogleSubject is null)
        {
            // Existing email/password account — link the Google identity.
            user.GoogleSubject = payload.Subject;
            user.EmailVerified = user.EmailVerified || payload.EmailVerified;
        }

        // Same gates as password login.
        if (user.IsLocked)
            return Results.Problem(statusCode: 423, title: "Account locked after too many failed attempts.");
        if (user.ApprovalStatus == ApprovalStatus.PendingApproval)
            return Results.Problem(statusCode: 403, title: "Application pending approval",
                detail: $"Your {user.Role} application is still being reviewed by the Localll team.");
        if (user.ApprovalStatus == ApprovalStatus.Rejected)
            return Results.Problem(statusCode: 403, title: "Application rejected",
                detail: user.RejectionReason ?? "Your partner application was not approved.");

        user.LastLoginAtUtc = DateTime.UtcNow;
        var refreshToken = tokens.CreateRefreshToken(user.Id);
        db.RefreshTokens.Add(refreshToken);
        await db.SaveChangesAsync(ct);

        var (accessToken, expiresAt) = tokens.CreateAccessToken(user);
        return Results.Ok(new AuthResponse(user.Id, user.Email, user.FullName, user.Role, accessToken, expiresAt, refreshToken.Token));
    }

    private static async Task<IResult> RefreshAsync(
        RefreshRequest request,
        IdentityDbContext db,
        TokenService tokens,
        CancellationToken ct)
    {
        var existing = await db.RefreshTokens.FirstOrDefaultAsync(t => t.Token == request.RefreshToken, ct);
        if (existing is null || !existing.IsActive)
            return Results.Unauthorized();

        var user = await db.Users.FirstAsync(u => u.Id == existing.UserId, ct);

        // Rotate: revoke the old token and issue a new one.
        var replacement = tokens.CreateRefreshToken(user.Id);
        existing.RevokedAtUtc = DateTime.UtcNow;
        existing.ReplacedByToken = replacement.Token;
        db.RefreshTokens.Add(replacement);
        await db.SaveChangesAsync(ct);

        var (accessToken, expiresAt) = tokens.CreateAccessToken(user);
        return Results.Ok(new AuthResponse(user.Id, user.Email, user.FullName, user.Role, accessToken, expiresAt, replacement.Token));
    }

    private static async Task<IResult> RequestOtpAsync(
        RequestOtpRequest request,
        ICacheService cache,
        IPublishEndpoint publisher,
        CancellationToken ct)
    {
        var code = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        await cache.SetStringAsync($"otp:phone:{request.PhoneNumber}", code, OtpTtl);

        // The Notification service owns actual SMS delivery.
        await publisher.Publish(new NotificationRequestedEvent(
            Guid.Empty, "Sms", "Localll verification code",
            $"Your Localll OTP is {code}. It expires in 5 minutes.", DateTime.UtcNow), ct);

        return Results.Accepted(value: new { message = "OTP sent." });
    }

    private static async Task<IResult> VerifyOtpAsync(
        VerifyOtpRequest request,
        ICacheService cache,
        IdentityDbContext db,
        CancellationToken ct)
    {
        var stored = await cache.GetStringAsync($"otp:phone:{request.PhoneNumber}");
        if (stored is null || stored != request.Code)
            return Results.BadRequest(new { error = "Invalid or expired OTP." });

        await cache.RemoveAsync($"otp:phone:{request.PhoneNumber}");

        var user = await db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == request.PhoneNumber, ct);
        if (user is not null)
        {
            user.PhoneVerified = true;
            await db.SaveChangesAsync(ct);
        }

        return Results.Ok(new { verified = true });
    }

    private static async Task<IResult> ForgotPasswordAsync(
        ForgotPasswordRequest request,
        IdentityDbContext db,
        ICacheService cache,
        IPublishEndpoint publisher,
        CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == request.Email.ToLowerInvariant(), ct);
        // Always return 202 so the endpoint can't be used to probe which emails exist.
        if (user is null) return Results.Accepted(value: new { message = "If the account exists, a reset code was sent." });

        var code = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        await cache.SetStringAsync($"pwreset:{user.Email}", code, TimeSpan.FromMinutes(15));

        await publisher.Publish(new NotificationRequestedEvent(
            user.Id, "Email", "Localll password reset",
            $"Your password reset code is {code}. It expires in 15 minutes.", DateTime.UtcNow), ct);

        return Results.Accepted(value: new { message = "If the account exists, a reset code was sent." });
    }

    private static async Task<IResult> ResetPasswordAsync(
        ResetPasswordRequest request,
        IdentityDbContext db,
        ICacheService cache,
        CancellationToken ct)
    {
        var email = request.Email.ToLowerInvariant();
        var stored = await cache.GetStringAsync($"pwreset:{email}");
        if (stored is null || stored != request.Code)
            return Results.BadRequest(new { error = "Invalid or expired reset code." });

        var user = await db.Users.Include(u => u.RefreshTokens).FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null) return Results.BadRequest(new { error = "Invalid or expired reset code." });

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.IsLocked = false;
        user.FailedLoginAttempts = 0;
        foreach (var token in user.RefreshTokens.Where(t => t.IsActive))
            token.RevokedAtUtc = DateTime.UtcNow;

        await cache.RemoveAsync($"pwreset:{email}");
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { message = "Password updated. Please log in again." });
    }

    private static async Task<IResult> LogoutAsync(
        RefreshRequest request,
        IdentityDbContext db,
        CancellationToken ct)
    {
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.Token == request.RefreshToken, ct);
        if (token is not null && token.IsActive)
        {
            token.RevokedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return Results.NoContent();
    }
}
