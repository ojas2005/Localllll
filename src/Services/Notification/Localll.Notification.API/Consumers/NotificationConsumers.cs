using Localll.Contracts;
using Localll.Notification.API.Channels;
using MassTransit;

namespace Localll.Notification.API.Consumers;

/// <summary>Routes explicit notification requests to the right channel.</summary>
public class NotificationRequestedConsumer(IEnumerable<INotificationChannel> channels)
    : IConsumer<NotificationRequestedEvent>
{
    public async Task Consume(ConsumeContext<NotificationRequestedEvent> context)
    {
        var message = context.Message;
        var channel = channels.FirstOrDefault(c =>
            string.Equals(c.Name, message.Channel, StringComparison.OrdinalIgnoreCase));

        if (channel is null)
            throw new InvalidOperationException($"Unknown notification channel '{message.Channel}'.");

        await channel.SendAsync(message.RecipientId, message.Subject, message.Body, context.CancellationToken);
    }
}

/// <summary>Domain events that always trigger a customer notification.</summary>
public class OrderLifecycleNotificationConsumer(IEnumerable<INotificationChannel> channels)
    : IConsumer<OrderCreatedEvent>, IConsumer<DeliveryCompletedEvent>, IConsumer<PaymentCompletedEvent>
{
    private INotificationChannel Push => channels.First(c => c.Name == "Push");

    public Task Consume(ConsumeContext<OrderCreatedEvent> context) =>
        Push.SendAsync(context.Message.CustomerId, "Order placed",
            $"Your {context.Message.OrderType} order ({context.Message.OrderId}) was placed. Amount: ₹{context.Message.Amount}.",
            context.CancellationToken);

    public Task Consume(ConsumeContext<DeliveryCompletedEvent> context) =>
        Push.SendAsync(context.Message.CustomerId, "Order delivered",
            $"Your order {context.Message.OrderId} has been delivered. Thanks for using Localll!",
            context.CancellationToken);

    public Task Consume(ConsumeContext<PaymentCompletedEvent> context) =>
        Push.SendAsync(context.Message.CustomerId, "Payment received",
            $"We received your payment of ₹{context.Message.Amount} via {context.Message.Method}.",
            context.CancellationToken);
}
