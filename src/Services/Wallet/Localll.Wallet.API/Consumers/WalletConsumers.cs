using Localll.Contracts;
using Localll.Wallet.API.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Localll.Wallet.API.Consumers;

/// <summary>Credits the delivery partner's wallet when a delivery completes.</summary>
public class DeliveryCompletedConsumer(WalletDbContext db, IPublishEndpoint publisher, ILogger<DeliveryCompletedConsumer> logger)
    : IConsumer<DeliveryCompletedEvent>
{
    public async Task Consume(ConsumeContext<DeliveryCompletedEvent> context)
    {
        var message = context.Message;

        // Idempotent: skip if this order already produced a ledger entry.
        var alreadyCredited = await db.LedgerEntries.AnyAsync(e => e.ReferenceId == message.OrderId);
        if (alreadyCredited) return;

        var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.OwnerId == message.PartnerId);
        if (wallet is null)
        {
            wallet = new Domain.Wallet { OwnerId = message.PartnerId, OwnerType = "Partner" };
            db.Wallets.Add(wallet);
        }

        var entry = wallet.Credit(message.PartnerEarning, "Delivery earning", message.OrderId);
        db.LedgerEntries.Add(entry); // explicit Add — see AuthEndpoints.LoginAsync
        await db.SaveChangesAsync();

        await publisher.Publish(new WalletUpdatedEvent(
            wallet.Id, wallet.OwnerId, wallet.Balance, "DeliveryEarning", DateTime.UtcNow));
        logger.LogInformation("Credited {Amount} to partner {PartnerId} for order {OrderId}",
            message.PartnerEarning, message.PartnerId, message.OrderId);
    }
}

/// <summary>Creates a customer wallet lazily on registration.</summary>
public class WalletUserRegisteredConsumer(WalletDbContext db) : IConsumer<UserRegisteredEvent>
{
    public async Task Consume(ConsumeContext<UserRegisteredEvent> context)
    {
        var message = context.Message;
        if (await db.Wallets.AnyAsync(w => w.OwnerId == message.UserId)) return;

        db.Wallets.Add(new Domain.Wallet
        {
            OwnerId = message.UserId,
            OwnerType = message.Role == "Customer" ? "Customer" : "Partner"
        });
        await db.SaveChangesAsync();
    }
}
