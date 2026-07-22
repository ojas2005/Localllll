namespace Localll.Contracts;

/// <summary>
/// Integration events published on the RabbitMQ bus. These are the only
/// cross-service contracts — services never touch each other's databases.
/// </summary>
public record UserRegisteredEvent(Guid UserId, string Email, string FullName, string Role, DateTime OccurredOnUtc);

public record OrderCreatedEvent(
    Guid OrderId,
    Guid CustomerId,
    string OrderType,          // Parcel | Grocery | Medicine
    decimal Amount,
    string PickupAddress,
    string DropAddress,
    DateTime OccurredOnUtc);

public record OrderAcceptedEvent(Guid OrderId, Guid PartnerId, DateTime OccurredOnUtc);

public record MedicineApprovedEvent(Guid OrderId, Guid PharmacyId, Guid CustomerId, decimal Amount, DateTime OccurredOnUtc);

public record PaymentCompletedEvent(
    Guid PaymentId,
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string Method,             // Upi | Card | Wallet
    DateTime OccurredOnUtc);

public record PaymentFailedEvent(Guid PaymentId, Guid OrderId, string Reason, DateTime OccurredOnUtc);

public record DeliveryCompletedEvent(
    Guid OrderId,
    Guid CustomerId,
    Guid PartnerId,
    decimal PartnerEarning,
    DateTime OccurredOnUtc);

public record WalletUpdatedEvent(Guid WalletId, Guid OwnerId, decimal NewBalance, string Reason, DateTime OccurredOnUtc);

public record OtpVerifiedEvent(Guid OrderId, Guid PartnerId, DateTime OccurredOnUtc);

public record AppointmentBookedEvent(Guid AppointmentId, Guid CustomerId, string ServiceType, DateTime ScheduledAtUtc, DateTime OccurredOnUtc);

public record PartnerRegisteredEvent(Guid PartnerId, string PartnerType, string Name, DateTime OccurredOnUtc); // Pharmacy | DeliveryPartner

public record NotificationRequestedEvent(
    Guid RecipientId,
    string Channel,            // Email | Sms | Push | WhatsApp
    string Subject,
    string Body,
    DateTime OccurredOnUtc);
