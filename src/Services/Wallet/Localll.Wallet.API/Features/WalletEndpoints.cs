using System.Security.Claims;
using Localll.Common.Auth;
using Localll.Contracts;
using Localll.Wallet.API.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Localll.Wallet.API.Features;

public record WithdrawRequest(decimal Amount, string BankAccountLast4);

public static class WalletEndpoints
{
    public static void MapWalletEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/wallets").WithTags("Wallets").RequireAuthorization();

        group.MapGet("/me", async (ClaimsPrincipal principal, WalletDbContext db) =>
        {
            var wallet = await db.Wallets.AsNoTracking()
                .FirstOrDefaultAsync(w => w.OwnerId == principal.GetUserId());
            return wallet is null ? Results.NotFound() : Results.Ok(new { wallet.Id, wallet.OwnerId, wallet.OwnerType, wallet.Balance });
        });

        group.MapGet("/me/ledger", async (ClaimsPrincipal principal, WalletDbContext db, int page = 1, int pageSize = 50) =>
        {
            var wallet = await db.Wallets.AsNoTracking()
                .FirstOrDefaultAsync(w => w.OwnerId == principal.GetUserId());
            if (wallet is null) return Results.NotFound();

            var entries = await db.LedgerEntries.AsNoTracking()
                .Where(e => e.WalletId == wallet.Id)
                .OrderByDescending(e => e.CreatedAtUtc)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .ToListAsync();
            return Results.Ok(entries);
        });

        group.MapPost("/me/withdraw", async (
            WithdrawRequest request,
            ClaimsPrincipal principal,
            WalletDbContext db,
            IPublishEndpoint publisher) =>
        {
            var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.OwnerId == principal.GetUserId());
            if (wallet is null) return Results.NotFound();

            var result = wallet.Debit(request.Amount, $"Withdrawal to bank ****{request.BankAccountLast4}");
            if (result.IsFailure)
                return Results.Conflict(new { error = result.Error.Message });

            db.LedgerEntries.Add(result.Value); // explicit Add — see AuthEndpoints.LoginAsync
            await db.SaveChangesAsync();
            await publisher.Publish(new WalletUpdatedEvent(
                wallet.Id, wallet.OwnerId, wallet.Balance, "Withdrawal", DateTime.UtcNow));

            return Results.Ok(new { wallet.Balance, entry = result.Value });
        });
    }
}
