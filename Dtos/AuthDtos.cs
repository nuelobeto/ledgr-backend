using System.ComponentModel.DataAnnotations;

namespace Api.Dtos;

public record RegisterRequest(
    [param: Required]
    [param: EmailAddress]
    string Email,

    [param: Required]
    string Password
);

public record LoginRequest(
    [param: Required]
    [param: EmailAddress]
    string Email,

    [param: Required]
    string Password
);

public record TwoFactorLoginRequest(
    [param: Required]
    string MfaToken,

    [param: Required]
    string Code
);

public record ForgotPasswordRequest(
    [param: Required]
    [param: EmailAddress]
    string Email
);
public record ResetPasswordRequest(
    [param: Required]
    string UserId,

    [param: Required]
    string Token,

    [param: Required]
    string NewPassword
);

public record ChangePasswordRequest(
    [param: Required]
    string CurrentPassword,

    [param: Required]
    string NewPassword
);

public record RefreshRequest(
    [param: Required]
    string RefreshToken
);

public record MfaVerifyRequest(
    [param: Required]
    string Code
);

public record MfaDisableRequest(
    [param: Required]
    string Code
);