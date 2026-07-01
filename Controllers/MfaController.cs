using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Api.Dtos;
using Api.Models;
using Api.Services;

namespace Api.Controllers;

[ApiController]
[Route("api/auth/mfa")]
[Authorize(AuthenticationSchemes = "Bearer,Enrollment")] // Bearer = a normal session; Enrollment = forced setup
public class MfaController(
  UserManager<User> userManager,
  ITokenService tokenService,
  IRefreshTokenService refreshTokenService,
  IAuditLogger auditLogger,
  IConfiguration config) : ControllerBase
{
  [HttpPost("enroll")]
  public async Task<IActionResult> Enroll()
  {
    var userId = User.FindFirstValue("sub");
    var user = await userManager.FindByIdAsync(userId!);
    if (user is null)
      return Unauthorized();

    if (await userManager.GetTwoFactorEnabledAsync(user))
      return BadRequest(new { message = "MFA is already enabled. Disable it first to re-enroll." });

    await userManager.ResetAuthenticatorKeyAsync(user); // fresh secret
    var key = await userManager.GetAuthenticatorKeyAsync(user);

    var issuer = config["MFA_ISSUER"] ?? config["JWT_ISSUER"] ?? "Api";
    var otpauthUri =
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(user.Email!)}" +
        $"?secret={key}&issuer={Uri.EscapeDataString(issuer)}&digits=6";

    return Ok(new
    {
      sharedKey = FormatKey(key!),
      otpauthUri
    });
  }

  [HttpPost("verify")]
  public async Task<IActionResult> Verify(MfaVerifyRequest request, CancellationToken ct)
  {
    var userId = User.FindFirstValue("sub");
    var user = await userManager.FindByIdAsync(userId!);
    if (user is null)
      return Unauthorized();

    var code = request.Code.Trim().Replace(" ", "");
    var isValid = await userManager.VerifyTwoFactorTokenAsync(
        user, TokenOptions.DefaultAuthenticatorProvider, code);

    if (!isValid)
      return BadRequest(new { message = "Invalid code. Check your authenticator app and try again." });

    await userManager.SetTwoFactorEnabledAsync(user, true);
    var recoveryCodes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);

    // Forced/first-time enrollment (came in on the enrollment token) has no session yet —
    // mint one now so the user walks straight into the app.
    if (User.FindFirstValue("purpose") == "mfa-enrollment")
    {
      user.LastLoginAtUtc = DateTime.UtcNow;
      await userManager.UpdateAsync(user);

      var roles = await userManager.GetRolesAsync(user);
      var accessToken = tokenService.CreateAccessToken(user, roles);
      var refreshToken = await refreshTokenService.IssueForNewSessionAsync(user, ct);

      return Ok(new { message = "MFA enabled.", recoveryCodes, accessToken, refreshToken });
    }

    await auditLogger.LogAsync(AuditEventType.MfaEnabled, user.Id);

    // Voluntary enrollment by an already-logged-in user — they keep their session.
    return Ok(new { message = "MFA enabled.", recoveryCodes });
  }

  private static string FormatKey(string key)
  {
    var sb = new StringBuilder();
    for (var i = 0; i < key.Length; i += 4)
      sb.Append(key.AsSpan(i, Math.Min(4, key.Length - i))).Append(' ');
    return sb.ToString().Trim().ToLowerInvariant();
  }

  [HttpPost("disable")]
  [Authorize(AuthenticationSchemes = "Bearer")] // a real session only — never an enrollment token
  public async Task<IActionResult> Disable(MfaDisableRequest request)
  {
    var userId = User.FindFirstValue("sub");
    var user = await userManager.FindByIdAsync(userId!);
    if (user is null)
      return Unauthorized();

    if (!await userManager.GetTwoFactorEnabledAsync(user))
      return BadRequest(new { message = "MFA is not enabled." });

    // Step-up: prove continued possession of the factor before removing it.
    var code = request.Code.Trim().Replace(" ", "");
    var isValid = await userManager.VerifyTwoFactorTokenAsync(
        user, TokenOptions.DefaultAuthenticatorProvider, code);

    if (!isValid)
    {
      var redeem = await userManager.RedeemTwoFactorRecoveryCodeAsync(user, code);
      isValid = redeem.Succeeded;
    }

    if (!isValid)
      return BadRequest(new { message = "Invalid authentication code." });

    await userManager.SetTwoFactorEnabledAsync(user, false);
    await userManager.ResetAuthenticatorKeyAsync(user); // kill the old secret
    await auditLogger.LogAsync(AuditEventType.MfaDisabled, user.Id);

    return Ok(new { message = "MFA disabled." });
  }

  [Authorize(AuthenticationSchemes = "Bearer")]
  [HttpPost("recovery-codes/regenerate")]
  public async Task<IActionResult> RegenerateRecoveryCodes(MfaVerifyRequest request, CancellationToken ct)
  {
    var userId = User.FindFirstValue("sub");
    var user = await userManager.FindByIdAsync(userId!);
    if (user is null)
      return Unauthorized();

    // You can only regenerate codes you actually have.
    if (!await userManager.GetTwoFactorEnabledAsync(user))
      return BadRequest(new { message = "MFA is not enabled on this account." });

    // Step-up: prove the authenticator before rolling the codes.
    var code = request.Code.Trim().Replace(" ", "");
    var isValid = await userManager.VerifyTwoFactorTokenAsync(
        user, TokenOptions.DefaultAuthenticatorProvider, code);

    if (!isValid)
    {
      await auditLogger.LogAsync(AuditEventType.MfaFailed, user.Id, ct: ct);
      return Unauthorized(new { message = "Invalid authentication code." });
    }

    var recoveryCodes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);

    await auditLogger.LogAsync(AuditEventType.RecoveryCodesRegenerated, user.Id, ct: ct);

    return Ok(new { recoveryCodes });
  }
}