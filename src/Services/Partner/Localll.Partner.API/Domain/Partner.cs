using Localll.SharedKernel;

namespace Localll.Partner.API.Domain;

public enum PartnerType
{
    Pharmacy,
    DeliveryPartner
}

public enum PartnerStatus
{
    PendingVerification,
    Active,
    Suspended
}

public class Partner : AggregateRoot
{
    public Guid UserId { get; set; }              // identity of the owning user account
    public PartnerType Type { get; set; }
    public PartnerStatus Status { get; set; } = PartnerStatus.PendingVerification;
    public string Name { get; set; } = string.Empty;
    public string? LicenseNumber { get; set; }    // pharmacies
    public string? VehicleNumber { get; set; }    // delivery partners
    public string City { get; set; } = string.Empty;
    public string? KycDocumentUrl { get; set; }   // object storage URL
    public bool IsOnline { get; set; }
    public decimal TotalEarnings { get; set; }
}

/// <summary>Pharmacy stock — owned here, surfaced to the Medicine service via events/APIs.</summary>
public class InventoryItem : Entity
{
    public Guid PharmacyId { get; set; }
    public string MedicineName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
}

/// <summary>A projection of open orders that partners can pick up, fed by OrderCreatedEvent.</summary>
public class AvailableOrder : Entity
{
    public Guid OrderId { get; set; }
    public string OrderType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PickupAddress { get; set; } = string.Empty;
    public string DropAddress { get; set; } = string.Empty;
    public Guid? AcceptedByPartnerId { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
}
