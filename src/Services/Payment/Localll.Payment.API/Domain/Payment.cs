using Localll.SharedKernel;

namespace Localll.Payment.API.Domain;

public enum PaymentStatus
{
    Pending,
    Completed,
    Failed,
    Refunded
}

public class Payment : AggregateRoot
{
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; } = "Upi";   // Upi | Card | Wallet
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? ProviderReference { get; set; }
    public string? FailureReason { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? RefundedAtUtc { get; set; }
}
