using Localll.Common.Extensions;
using Localll.Notification.API.Channels;
using Localll.Notification.API.Consumers;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatform("notification-service", bus =>
{
    bus.AddConsumer<NotificationRequestedConsumer>();
    bus.AddConsumer<OrderLifecycleNotificationConsumer>();
});

builder.Services.AddSingleton<INotificationChannel, EmailChannel>();
builder.Services.AddSingleton<INotificationChannel, SmsChannel>();
builder.Services.AddSingleton<INotificationChannel, PushChannel>();
builder.Services.AddSingleton<INotificationChannel, WhatsAppChannel>();

var app = builder.Build();

app.UsePlatform();
app.Run();
