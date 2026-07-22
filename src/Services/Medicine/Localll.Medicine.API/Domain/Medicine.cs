using Localll.SharedKernel;

namespace Localll.Medicine.API.Domain;

public class Medicine : Entity
{
    public string Name { get; set; } = string.Empty;
    public string? GenericName { get; set; }
    public string? Manufacturer { get; set; }
    public decimal Price { get; set; }
    public bool RequiresPrescription { get; set; }
    public string? Category { get; set; }
}

public enum MedicineOrderStatus
{
    PendingApproval,   // prescription uploaded, waiting for a pharmacist
    Approved,
    Rejected,
    AwaitingPayment,
    Paid,
    Dispatched,
    Delivered,
    Cancelled
}

public class MedicineOrder : AggregateRoot
{
    public Guid CustomerId { get; set; }
    public Guid? PharmacyId { get; set; }
    public MedicineOrderStatus Status { get; set; } = MedicineOrderStatus.PendingApproval;
    public string DeliveryAddress { get; set; } = string.Empty;
    public string? PrescriptionUrl { get; set; }   // object storage URL — never stored in Postgres as bytes
    public string? RejectionReason { get; set; }
    public decimal TotalAmount { get; set; }

    public List<MedicineOrderItem> Items { get; set; } = [];
}

public class MedicineOrderItem : Entity
{
    public Guid OrderId { get; set; }
    public Guid MedicineId { get; set; }
    public string MedicineName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
