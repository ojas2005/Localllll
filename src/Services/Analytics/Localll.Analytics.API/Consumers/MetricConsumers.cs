using Localll.Analytics.API.Data;
using Localll.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Localll.Analytics.API.Consumers;

public static class MetricNames
{
    public const string OrdersCreated = "OrdersCreated";
    public const string Revenue = "Revenue";
    public const string DeliveriesCompleted = "DeliveriesCompleted";
    public const string NewUsers = "NewUsers";
    public const string NewPartners = "NewPartners";
}

public class MetricConsumers(AnalyticsDbContext db) :
    IConsumer<OrderCreatedEvent>,
    IConsumer<PaymentCompletedEvent>,
    IConsumer<DeliveryCompletedEvent>,
    IConsumer<UserRegisteredEvent>,
    IConsumer<PartnerRegisteredEvent>
{
    public Task Consume(ConsumeContext<OrderCreatedEvent> context) =>
        IncrementAsync(MetricNames.OrdersCreated, 1);

    public Task Consume(ConsumeContext<PaymentCompletedEvent> context) =>
        IncrementAsync(MetricNames.Revenue, context.Message.Amount);

    public Task Consume(ConsumeContext<DeliveryCompletedEvent> context) =>
        IncrementAsync(MetricNames.DeliveriesCompleted, 1);

    public Task Consume(ConsumeContext<UserRegisteredEvent> context) =>
        IncrementAsync(MetricNames.NewUsers, 1);

    public Task Consume(ConsumeContext<PartnerRegisteredEvent> context) =>
        IncrementAsync(MetricNames.NewPartners, 1);

    private async Task IncrementAsync(string metric, decimal delta)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var affected = await db.DailyMetrics
            .Where(m => m.Date == today && m.Metric == metric)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Value, m => m.Value + delta));

        if (affected == 0)
        {
            db.DailyMetrics.Add(new DailyMetric { Date = today, Metric = metric, Value = delta });
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Another consumer inserted the row concurrently — retry as an update.
                db.ChangeTracker.Clear();
                await db.DailyMetrics
                    .Where(m => m.Date == today && m.Metric == metric)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.Value, m => m.Value + delta));
            }
        }
    }
}
