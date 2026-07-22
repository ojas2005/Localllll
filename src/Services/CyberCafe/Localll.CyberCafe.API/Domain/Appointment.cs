using Localll.SharedKernel;

namespace Localll.CyberCafe.API.Domain;

public enum AppointmentStatus
{
    Booked,
    OperatorAssigned,
    InProgress,
    Completed,
    Cancelled
}

public class Appointment : AggregateRoot
{
    public Guid CustomerId { get; set; }
    public string ServiceType { get; set; } = string.Empty; // e.g. GovForm, PanCard, PrintScan
    public DateTime ScheduledAtUtc { get; set; }
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Booked;
    public Guid? OperatorId { get; set; }
    public string? VideoSessionId { get; set; }             // external RTC session id
    public string? Notes { get; set; }
    public List<SessionFile> Files { get; set; } = [];
}

public class SessionFile : Entity
{
    public Guid AppointmentId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string StorageUrl { get; set; } = string.Empty;  // object storage — no blobs in Postgres
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}
