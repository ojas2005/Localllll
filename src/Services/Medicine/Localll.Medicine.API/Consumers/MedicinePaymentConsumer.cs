using Localll.Contracts;
using Localll.Medicine.API.Data;
using Localll.Medicine.API.Domain;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Localll.Medicine.API.Consumers;

public class MedicinePaymentConsumer(MedicineDbContext db, ILogger<MedicinePaymentConsumer> logger)
    : IConsumer<PaymentCompletedEvent>
{
    public async Task Consume(ConsumeContext<PaymentCompletedEvent> context)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == context.Message.OrderId);
        if (order is null || order.Status != MedicineOrderStatus.AwaitingPayment) return;

        order.Status = MedicineOrderStatus.Paid;
        order.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        logger.LogInformation("Medicine order {OrderId} marked as paid", order.Id);
    }
}
