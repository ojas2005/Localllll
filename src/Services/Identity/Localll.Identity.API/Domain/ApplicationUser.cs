using Localll.SharedKernel;

namespace Localll.Identity.API.Domain;

public static class Roles
{
    public const string Customer = "Customer";
    public const string DeliveryPartner = "DeliveryPartner";
    public const string PharmacyPartner = "PharmacyPartner";
    public const string CyberCafeOperator = "CyberCafeOperator";
    public const string Admin = "Admin";

    public static readonly string[] All = [Customer, DeliveryPartner, PharmacyPartner, CyberCafeOperator, Admin];

    /// <summary>Roles a visitor may apply for; everything except Customer needs owner approval.</summary>
    public static readonly string[] SelfRegisterable = [Customer, DeliveryPartner, PharmacyPartner, CyberCafeOperator];
    public static bool RequiresApproval(string role) => role != Customer && role != Admin;
}

public enum ApprovalStatus
{
    Approved,          // customers and admin-created accounts
    PendingApproval,   // partner roles awaiting owner review
    Rejected
}

public class ApplicationUser : AggregateRoot
{
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = Roles.Customer;
    public ApprovalStatus ApprovalStatus { get; set; } = ApprovalStatus.Approved;
    public string? ApplicationNote { get; set; }        // free text the applicant submits
    public DateTime? ReviewedAtUtc { get; set; }
    public Guid? ReviewedBy { get; set; }
    public string? RejectionReason { get; set; }
    public string? GoogleSubject { get; set; }          // Google account id when signed up via Google
    public bool PhoneVerified { get; set; }
    public bool EmailVerified { get; set; }
    public bool MfaEnabled { get; set; }
    public bool IsLocked { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }

    public List<RefreshToken> RefreshTokens { get; set; } = [];
}

public class RefreshToken : Entity
{
    public Guid UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? ReplacedByToken { get; set; }

    public bool IsActive => RevokedAtUtc is null && DateTime.UtcNow < ExpiresAtUtc;
}
