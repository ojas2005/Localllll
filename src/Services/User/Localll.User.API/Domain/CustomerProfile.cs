using Localll.SharedKernel;

namespace Localll.User.API.Domain;

public class CustomerProfile : AggregateRoot
{
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? PreferredLanguage { get; set; }
    public List<Address> Addresses { get; set; } = [];
}

public class Address : Entity
{
    public Guid ProfileId { get; set; }
    public string Label { get; set; } = "Home";      // Home | Work | Other
    public string Line1 { get; set; } = string.Empty;
    public string? Line2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public bool IsDefault { get; set; }
}

public class Review : Entity
{
    public Guid CustomerId { get; set; }
    public Guid OrderId { get; set; }
    public string TargetType { get; set; } = string.Empty; // DeliveryPartner | Pharmacy | Service
    public Guid TargetId { get; set; }
    public int Rating { get; set; }                         // 1..5
    public string? Comment { get; set; }
}
