using Localll.Contracts;
using Localll.Partner.API.Data;
using Localll.Partner.API.Domain;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Localll.Partner.API.Consumers;

/// <summary>Projects newly created orders into the "available orders" feed for delivery partners.</summary>
public class OrderCreatedProjectionConsumer(PartnerDbContext db) : IConsumer<OrderCreatedEvent>
{
    public async Task Consume(ConsumeContext<OrderCreatedEvent> context)
    {
        var message = context.Message;
        if (await db.AvailableOrders.AnyAsync(o => o.OrderId == message.OrderId)) return;

        db.AvailableOrders.Add(new AvailableOrder
        {
            OrderId = message.OrderId,
            OrderType = message.OrderType,
            Amount = message.Amount,
            PickupAddress = message.PickupAddress,
            DropAddress = message.DropAddress
        });
        await db.SaveChangesAsync();
    }
}

/// <summary>
/// Seeds a minimal partner profile when a partner-role account is approved, so
/// customer-facing surfaces (live tracking) can show the partner's name even
/// before they complete full onboarding.
/// </summary>
public class PartnerUserRegisteredConsumer(PartnerDbContext db) : IConsumer<UserRegisteredEvent>
{
    public async Task Consume(ConsumeContext<UserRegisteredEvent> context)
    {
        var message = context.Message;
        var type = message.Role switch
        {
            "DeliveryPartner" => PartnerType.DeliveryPartner,
            "PharmacyPartner" => PartnerType.Pharmacy,
            _ => (PartnerType?)null,
        };
        if (type is null) return;
        if (await db.Partners.AnyAsync(p => p.UserId == message.UserId)) return;

        db.Partners.Add(new Domain.Partner
        {
            UserId = message.UserId,
            Type = type.Value,
            Name = message.FullName,
            Status = PartnerStatus.Active,
            City = string.Empty,
        });
        await db.SaveChangesAsync();
    }
}

/// <summary>Keeps the partner's lifetime earnings figure up to date.</summary>
public class PartnerDeliveryCompletedConsumer(PartnerDbContext db) : IConsumer<DeliveryCompletedEvent>
{
    public async Task Consume(ConsumeContext<DeliveryCompletedEvent> context)
    {
        var partner = await db.Partners.FirstOrDefaultAsync(p => p.UserId == context.Message.PartnerId);
        if (partner is null) return;

        partner.TotalEarnings += context.Message.PartnerEarning;
        partner.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
}
