using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Api.Dtos;
using Api.Models;
using Api.Services;

namespace Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
  UserManager<User> userManager,
  SignInManager<User> signInManager,
  ITokenService tokenService,
  IRefreshTokenService refreshTokenService,
  IEmailSender emailSender,
  IAuditLogger auditLogger,
  IConfiguration config) : ControllerBase
{
  [EnableRateLimiting("login")]
  [HttpPost("register")]
  public async Task<IActionResult> Register(RegisterRequest request)
  {
    var existing = await userManager.FindByEmailAsync(request.Email);
    if (existing is null)
    {
      var user = new User { Email = request.Email, UserName = request.Email };

      var result = await userManager.CreateAsync(user, request.Password);
      if (!result.Succeeded)
        return BadRequest(result.Errors.Select(e => e.Description));

      var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
      var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
      var link = $"{config["API_URL"]}/api/auth/confirm-email?userId={user.Id}&token={encoded}";

      await emailSender.SendAsync(user.Email!, "Confirm your email",
        $"Confirm your account: <a href=\"{link}\">click here</a>");
    }

    // Same response whether or not the email already existed
    return Ok(new { message = "If that email isn't already registered, a confirmation link has been sent." });
  }

  [HttpGet("confirm-email")]
  public async Task<IActionResult> ConfirmEmail(string userId, string token, CancellationToken ct)
  {
    // This is the link the user clicks straight out of their inbox — a browser navigation,
    // not an XHR/fetch call. So it can't just return JSON: it needs to land the user on an
    // actual page. Do the verification here (that part has to stay server-side, it consumes
    // a one-time token), then hand off to the frontend's result page via a 302.
    var frontendUrl = config["APP_URL"];

    var user = await userManager.FindByIdAsync(userId);
    if (user is null)
      return Redirect($"{frontendUrl}/auth/email-confirmed?status=error");

    string decoded;
    try
    {
      decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
    }
    catch (FormatException)
    {
      return Redirect($"{frontendUrl}/auth/email-confirmed?status=error");
    }

    var result = await userManager.ConfirmEmailAsync(user, decoded);
    if (!result.Succeeded)
      return Redirect($"{frontendUrl}/auth/email-confirmed?status=error");

    await auditLogger.LogAsync(AuditEventType.EmailConfirmed, user.Id, ct: ct);

    return Redirect($"{frontendUrl}/auth/email-confirmed?status=success");
  }

  [EnableRateLimiting("login")]
  [HttpPost("login")]
  public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
  {
    var user = await userManager.FindByEmailAsync(request.Email);
    if (user is null || user.IsDeleted || user.Status == UserStatus.Suspended)
    {
      await auditLogger.LogAsync(AuditEventType.LoginFailed, user?.Id, detail: $"email={request.Email}", ct: ct);
      return Unauthorized(new { message = "Invalid credentials." });
    }

    var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);

    if (result.IsLockedOut)
    {
      await auditLogger.LogAsync(AuditEventType.LoginLockedOut, user.Id, ct: ct);
      return Unauthorized(new { message = "Account temporarily locked. Try again later." });
    }
    if (result.IsNotAllowed)
    {
      await auditLogger.LogAsync(AuditEventType.LoginFailed, user.Id, detail: "email not confirmed", ct: ct);
      return Unauthorized(new { message = "Please confirm your email before logging in." });
    }
    if (!result.Succeeded)
    {
      await auditLogger.LogAsync(AuditEventType.LoginFailed, user.Id, detail: "bad password", ct: ct);
      return Unauthorized(new { message = "Invalid credentials." });
    }

    // Enrolled → demand the second factor.
    if (await userManager.GetTwoFactorEnabledAsync(user))
    {
      await auditLogger.LogAsync(AuditEventType.MfaChallengeIssued, user.Id, ct: ct);
      var mfaToken = tokenService.CreateMfaChallengeToken(user);
      return Ok(new { mfaRequired = true, mfaToken });
    }

    // Not enrolled but the app mandates MFA → force enrollment, no session yet.
    if (config.GetValue<bool>("MFA_REQUIRED"))
    {
      var enrollmentToken = tokenService.CreateEnrollmentToken(user);
      return Ok(new { enrollmentRequired = true, enrollmentToken });
    }

    // Not enrolled, MFA optional → issue a session.
    return await IssueSessionAsync(user, ct);
  }

  [EnableRateLimiting("mfa")]
  [HttpPost("login/2fa")]
  public async Task<IActionResult> LoginTwoFactor(TwoFactorLoginRequest request, CancellationToken ct)
  {
    var userId = tokenService.ValidateMfaChallengeToken(request.MfaToken);
    if (userId is null)
      return Unauthorized(new { message = "Invalid or expired MFA session. Please log in again." });

    var user = await userManager.FindByIdAsync(userId);
    if (user is null || user.IsDeleted || user.Status == UserStatus.Suspended)
      return Unauthorized(new { message = "Invalid credentials." });

    var code = request.Code.Trim().Replace(" ", "");

    var isValid = await userManager.VerifyTwoFactorTokenAsync(
        user, TokenOptions.DefaultAuthenticatorProvider, code);

    if (!isValid)
    {
      var redeem = await userManager.RedeemTwoFactorRecoveryCodeAsync(user, code);
      isValid = redeem.Succeeded;
    }

    if (!isValid)
    {
      await auditLogger.LogAsync(AuditEventType.MfaFailed, user.Id, ct: ct);
      return Unauthorized(new { message = "Invalid authentication code." });
    }

    return await IssueSessionAsync(user, ct);
  }

  private async Task<IActionResult> IssueSessionAsync(User user, CancellationToken ct)
  {
    user.LastLoginAtUtc = DateTime.UtcNow;
    await userManager.UpdateAsync(user);

    var roles = await userManager.GetRolesAsync(user);
    var accessToken = tokenService.CreateAccessToken(user, roles);
    var refreshToken = await refreshTokenService.IssueForNewSessionAsync(user, ct);

    await auditLogger.LogAsync(AuditEventType.LoginSucceeded, user.Id, ct: ct);
    return Ok(new { accessToken, refreshToken });
  }

  [EnableRateLimiting("password-reset")]
  [HttpPost("forgot-password")]
  public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request)
  {
    var user = await userManager.FindByEmailAsync(request.Email);
    if (user is not null && !user.IsDeleted)
    {
      var token = await userManager.GeneratePasswordResetTokenAsync(user);
      var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
      var link = $"{config["APP_URL"]}/auth/reset-password?userId={user.Id}&token={encoded}";
      await emailSender.SendAsync(user.Email!, "Reset your password",
        $"Reset your password: <a href=\"{link}\">click here</a>");
      await auditLogger.LogAsync(AuditEventType.PasswordResetRequested, user.Id);
    }

    return Ok(new { message = "If an account exists for that email, a reset link has been sent." });
  }

  [EnableRateLimiting("password-reset")]
  [HttpPost("reset-password")]
  public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
  {
    var user = await userManager.FindByIdAsync(request.UserId);
    if (user is null || user.IsDeleted)
      return BadRequest(new { message = "Invalid or expired reset link." });

    string decoded;
    try { decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Token)); }
    catch (FormatException) { return BadRequest(new { message = "Invalid or expired reset link." }); }

    var result = await userManager.ResetPasswordAsync(user, decoded, request.NewPassword);
    if (!result.Succeeded)
    {
      if (result.Errors.Any(e => e.Code == "InvalidToken"))
        return BadRequest(new { message = "Invalid or expired reset link." });
      return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
    }

    await auditLogger.LogAsync(AuditEventType.PasswordReset, user.Id);

    return Ok(new { message = "Password reset. You can now log in with your new password." });
  }

  [Authorize]
  [HttpPost("change-password")]
  public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
  {
    var userId = User.FindFirstValue("sub");
    var user = await userManager.FindByIdAsync(userId!);
    if (user is null)
      return Unauthorized();

    var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
    if (!result.Succeeded)
    {
      if (result.Errors.Any(e => e.Code == "PasswordMismatch"))
        return BadRequest(new { message = "Current password is incorrect." });
      return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
    }

    await auditLogger.LogAsync(AuditEventType.PasswordChanged, user.Id);

    return Ok(new { message = "Password changed." });
  }

  [HttpPost("refresh")]
  public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken ct)
  {
    var result = await refreshTokenService.ValidateAndRotateAsync(request.RefreshToken, ct);
    if (!result.Succeeded)
      return Unauthorized(new { message = "Invalid or expired refresh token." });

    var user = result.User!;
    var roles = await userManager.GetRolesAsync(user);
    var accessToken = tokenService.CreateAccessToken(user, roles);

    return Ok(new { accessToken, refreshToken = result.RefreshToken });
  }

  [HttpPost("logout")]
  public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken ct)
  {
    await refreshTokenService.RevokeByTokenAsync(request.RefreshToken, ct);

    await auditLogger.LogAsync(AuditEventType.SessionsRevoked, ct: ct);

    return Ok(new { message = "Logged out." });
  }
}