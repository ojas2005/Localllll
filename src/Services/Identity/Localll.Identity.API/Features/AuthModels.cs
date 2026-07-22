using FluentValidation;

namespace Localll.Identity.API.Features;

public record RegisterRequest(string Email, string PhoneNumber, string FullName, string Password, string? Role, string? ApplicationNote);
public record LoginRequest(string Email, string Password);
public record GoogleLoginRequest(string IdToken);

/// <summary>Returned instead of tokens when a partner-role application needs owner approval.</summary>
public record PendingApplicationResponse(string Status, string AppliedRole, string Message);

public record PartnerApplicationDto(
    Guid UserId,
    string FullName,
    string Email,
    string PhoneNumber,
    string AppliedRole,
    string? ApplicationNote,
    string Status,
    DateTime AppliedAtUtc);

public record ReviewApplicationRequest(bool Approved, string? RejectionReason);
public record RefreshRequest(string RefreshToken);
public record RequestOtpRequest(string PhoneNumber);
public record VerifyOtpRequest(string PhoneNumber, string Code);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Email, string Code, string NewPassword);

public record AuthResponse(
    Guid UserId,
    string Email,
    string FullName,
    string Role,
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken);

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.PhoneNumber).NotEmpty().Matches(@"^\+?[0-9]{10,15}$");
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");
    }
}

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}
