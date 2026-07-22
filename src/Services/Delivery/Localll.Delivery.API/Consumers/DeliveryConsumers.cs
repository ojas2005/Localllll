using Localll.Contracts;
using Localll.Delivery.API.Data;
using Localll.Delivery.API.Domain;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Localll.Delivery.API.Consumers;

/// <summary>Payment cleared — order can now be offered to delivery partners.</summary>
public class PaymentCompletedConsumer(DeliveryDbContext db, ILogger<PaymentCompletedConsumer> logger)
    : IConsumer<PaymentCompletedEvent>
{
    public async Task Consume(ConsumeContext<PaymentCompletedEvent> context)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == context.Message.OrderId);
        if (order is null || order.Status != DeliveryStatus.AwaitingPayment) return;

        order.Status = DeliveryStatus.ReadyForPickup;
        order.UpdatedAtUtc = DateTime.UtcNow;
        db.TrackingEvents.Add(new TrackingEvent { OrderId = order.Id, Status = "PaymentReceived" });
        await db.SaveChangesAsync();
        logger.LogInformation("Order {OrderId} is ready for pickup", order.Id);
    }
}

/// <summary>A delivery partner accepted the order in the Partner service.</summary>
public class OrderAcceptedConsumer(DeliveryDbContext db, ILogger<OrderAcceptedConsumer> logger)
    : IConsumer<OrderAcceptedEvent>
{
    public async Task Consume(ConsumeContext<OrderAcceptedEvent> context)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == context.Message.OrderId);
        if (order is null || order.PartnerId is not null) return;

        order.PartnerId = context.Message.PartnerId;
        order.Status = DeliveryStatus.Assigned;
        order.AssignedAtUtc = DateTime.UtcNow;
        order.UpdatedAtUtc = DateTime.UtcNow;
        db.TrackingEvents.Add(new TrackingEvent { OrderId = order.Id, Status = "PartnerAssigned" });
        await db.SaveChangesAsync();
        logger.LogInformation("Order {OrderId} assigned to partner {PartnerId}", order.Id, context.Message.PartnerId);
    }
}
