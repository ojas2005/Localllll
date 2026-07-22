using Localll.SharedKernel;

namespace Localll.StoreOrders.API.Domain;

/// <summary>The exact lifecycle from the manual-payment / assignment workflow.</summary>
public enum StoreOrderStatus
{
    Created,
    PendingPaymentVerification, // UPI: screenshot uploaded, awaiting admin
    PendingAdminApproval,       // COD: awaiting admin
    PaymentRejected,            // admin rejected
    Approved,                   // admin approved (transient → WaitingForDeliveryPartner)
    WaitingForDeliveryPartner,  // visible to all partners, first-come-first-served
    Assigned,                   // a partner locked it
    CollectedFromStore,
    OutForDelivery,
    Delivered,
    StoreCredited,              // settlement done
    Cancelled
}

public enum PaymentMethod
{
    Upi,
    Cod
}

public class Store : Entity
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string UpiId { get; set; } = string.Empty;          // merchant UPI for payments
    public Guid? OwnerUserId { get; set; }                      // PharmacyPartner who owns it
    public List<StoreProduct> Products { get; set; } = [];
}

public class StoreProduct : Entity
{
    public Guid StoreId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string UnitLabel { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public bool InStock { get; set; } = true;
}

public class StoreOrder : AggregateRoot
{
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string DeliveryAddress { get; set; } = string.Empty;

    public Guid StoreId { get; set; }
    public string StoreName { get; set; } = string.Empty;
    public string StoreAddress { get; set; } = string.Empty;

    public StoreOrderStatus Status { get; set; } = StoreOrderStatus.Created;
    public PaymentMethod PaymentMethod { get; set; }
    public string? PaymentScreenshotUrl { get; set; }          // UPI proof (object/file URL)
    public string? RejectionReason { get; set; }

    public decimal ItemsTotal { get; set; }
    public decimal ServiceCharge { get; set; }
    public decimal DeliveryCharge { get; set; }
    public decimal GrandTotal { get; set; }

    // Settlement figures (computed at delivery).
    public decimal PlatformCommission { get; set; }
    public decimal StorePayout { get; set; }

    public Guid? DeliveryPartnerId { get; set; }
    public string? DeliveryPartnerName { get; set; }
    public decimal PartnerEarning { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? AssignedAtUtc { get; set; }
    public DateTime? CollectedAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }

    public List<StoreOrderItem> Items { get; set; } = [];
}

public class StoreOrderItem : Entity
{
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal => Quantity * UnitPrice;
}

/// <summary>Per-store settlement wallet. Credited only after successful delivery.</summary>
public class StoreWallet : AggregateRoot
{
    public Guid StoreId { get; set; }
    public decimal Balance { get; private set; }
    public List<StoreWalletEntry> Entries { get; set; } = [];

    public StoreWalletEntry Credit(decimal amount, string reason, Guid? orderId)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        Balance += amount;
        return Append(StoreWalletEntryType.Credit, amount, reason, orderId);
    }

    public Result<StoreWalletEntry> Debit(decimal amount, string reason)
    {
        if (amount <= 0) return Result.Failure<StoreWalletEntry>(Error.Validation("Amount must be positive."));
        if (Balance < amount) return Result.Failure<StoreWalletEntry>(Error.Conflict("Insufficient balance."));
        Balance -= amount;
        return Result.Success(Append(StoreWalletEntryType.Debit, amount, reason, null));
    }

    private StoreWalletEntry Append(StoreWalletEntryType type, decimal amount, string reason, Guid? orderId)
    {
        var entry = new StoreWalletEntry
        {
            WalletId = Id, Type = type, Amount = amount,
            BalanceAfter = Balance, Reason = reason, OrderId = orderId,
        };
        Entries.Add(entry);
        UpdatedAtUtc = DateTime.UtcNow;
        return entry;
    }
}

public enum StoreWalletEntryType { Credit, Debit }

public class StoreWalletEntry : Entity
{
    public Guid WalletId { get; set; }
    public StoreWalletEntryType Type { get; set; }
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? OrderId { get; set; }
}

/// <summary>Central place for the money rules so they are testable and consistent.</summary>
public static class Settlement
{
    public const decimal CommissionRate = 0.15m;  // platform keeps 15% of items total
    public const decimal DeliveryCharge = 30m;     // flat, paid to the delivery partner

    public static decimal ServiceChargeFor(decimal itemsTotal) => Math.Round(itemsTotal * 0.02m, 2); // 2% handling
    public static decimal Commission(decimal itemsTotal) => Math.Round(itemsTotal * CommissionRate, 2);

    /// <summary>Store receives items total minus commission (delivery charge is the partner's).</summary>
    public static decimal StorePayout(decimal itemsTotal) => Math.Round(itemsTotal - Commission(itemsTotal), 2);
}
