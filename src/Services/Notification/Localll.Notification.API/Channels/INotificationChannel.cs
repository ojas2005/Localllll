namespace Localll.Notification.API.Channels;

public interface INotificationChannel
{
    string Name { get; }
    Task SendAsync(Guid recipientId, string subject, string body, CancellationToken ct);
}

/// <summary>
/// Dev implementations log the message instead of calling a provider.
/// Swap these for SendGrid / MSG91 / FCM / WhatsApp Business API in production.
/// </summary>
public class EmailChannel(ILogger<EmailChannel> logger) : INotificationChannel
{
    public string Name => "Email";
    public Task SendAsync(Guid recipientId, string subject, string body, CancellationToken ct)
    {
        logger.LogInformation("[EMAIL → {Recipient}] {Subject}: {Body}", recipientId, subject, body);
        return Task.CompletedTask;
    }
}

public class SmsChannel(ILogger<SmsChannel> logger) : INotificationChannel
{
    public string Name => "Sms";
    public Task SendAsync(Guid recipientId, string subject, string body, CancellationToken ct)
    {
        logger.LogInformation("[SMS → {Recipient}] {Body}", recipientId, body);
        return Task.CompletedTask;
    }
}

public class PushChannel(ILogger<PushChannel> logger) : INotificationChannel
{
    public string Name => "Push";
    public Task SendAsync(Guid recipientId, string subject, string body, CancellationToken ct)
    {
        logger.LogInformation("[PUSH → {Recipient}] {Subject}: {Body}", recipientId, subject, body);
        return Task.CompletedTask;
    }
}

public class WhatsAppChannel(ILogger<WhatsAppChannel> logger) : INotificationChannel
{
    public string Name => "WhatsApp";
    public Task SendAsync(Guid recipientId, string subject, string body, CancellationToken ct)
    {
        logger.LogInformation("[WHATSAPP → {Recipient}] {Body}", recipientId, body);
        return Task.CompletedTask;
    }
}
