using Localll.SharedKernel;

namespace Localll.Delivery.API.Domain;

public enum DeliveryStatus
{
    Created,
    AwaitingPayment,
    ReadyForPickup,
    Assigned,
    PickedUp,
    Delivered,
    Cancelled
}

public enum DeliveryOrderType
{
    Parcel,
    Grocery
}

public class DeliveryOrder : AggregateRoot
{
    public Guid CustomerId { get; set; }
    public DeliveryOrderType OrderType { get; set; }
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Created;

    public string PickupAddress { get; set; } = string.Empty;
    public string DropAddress { get; set; } = string.Empty;
    public double DistanceKm { get; set; }
    public double WeightKg { get; set; }
    public string? ItemsDescription { get; set; }

    public decimal Charge { get; set; }
    public Guid? PartnerId { get; set; }
    public DateTime? AssignedAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
}

public class TrackingEvent : Entity
{
    public Guid OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Note { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

/// <summary>Browsable grocery catalog — customers build a cart from these items.</summary>
public class GroceryItem : Entity
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;   // Staples | Dairy | Produce | Snacks | Household
    public string UnitLabel { get; set; } = string.Empty;  // e.g. "1 kg", "500 g", "1 L"
    public decimal Price { get; set; }
    public double WeightKg { get; set; }                   // shipping weight per unit
    public bool InStock { get; set; } = true;
}

/// <summary>Weight + distance based pricing. Kept pure so it is unit-testable.</summary>
public static class DeliveryPricing
{
    public const decimal BaseCharge = 30m;
    public const decimal PerKm = 8m;
    public const decimal PerKgOverFirstKg = 10m;
    public const decimal GrocerySurcharge = 15m;

    public static decimal Calculate(DeliveryOrderType type, double distanceKm, double weightKg)
    {
        if (distanceKm < 0) throw new ArgumentOutOfRangeException(nameof(distanceKm));
        if (weightKg < 0) throw new ArgumentOutOfRangeException(nameof(weightKg));

        var charge = BaseCharge
                     + (decimal)distanceKm * PerKm
                     + (decimal)Math.Max(0, weightKg - 1) * PerKgOverFirstKg;

        if (type == DeliveryOrderType.Grocery) charge += GrocerySurcharge;
        return Math.Round(charge, 2);
    }
}
