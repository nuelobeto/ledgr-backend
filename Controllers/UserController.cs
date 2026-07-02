using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Api.Dtos;
using Api.Models;
using Api.Services;

namespace Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public class UserController(
  UserManager<User> userManager,
  IRefreshTokenService refreshTokenService,
  IAuditLogger auditLogger
) : ControllerBase
{
  [HttpGet("me")]
  public async Task<IActionResult> Me()
  {
    var userId = User.FindFirstValue("sub");
    if (userId is null)
      return Unauthorized();

    var user = await userManager.FindByIdAsync(userId);
    if (user is null)
      return Unauthorized();

    var roles = await userManager.GetRolesAsync(user);

    return Ok(user.ToResponse(roles));
  }

  [HttpGet("{id:guid}")]
  [Authorize(Roles = "Admin")]
  public async Task<IActionResult> GetById(Guid id)
  {
    var user = await userManager.Users.FirstOrDefaultAsync(u => u.Id == id);
    if (user is null)
      return NotFound();

    var roles = await userManager.GetRolesAsync(user);

    return Ok(user.ToResponse(roles));
  }

  [HttpGet]
  [Authorize(Roles = "Admin")]
  public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
  {
    page = Math.Max(page, 1);
    pageSize = Math.Clamp(pageSize, 1, 100);

    var query = userManager.Users.OrderBy(u => u.CreatedAtUtc);
    var totalCount = await query.CountAsync();

    var users = await query
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync();

    var items = new List<UserResponse>(users.Count);
    foreach (var user in users) // N+1: one GetRolesAsync per row
    {
      var roles = await userManager.GetRolesAsync(user);
      items.Add(user.ToResponse(roles));
    }

    return Ok(new PagedResponse<UserResponse>(items, page, pageSize, totalCount));
  }

  [HttpPost("{id:guid}/suspend")]
  [Authorize(Roles = "Admin")]
  public async Task<IActionResult> Suspend(Guid id, CancellationToken ct)
  {
    if (User.FindFirstValue("sub") == id.ToString())
      return BadRequest(new { message = "You can't suspend your own account." });

    var user = await userManager.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
    if (user is null)
      return NotFound();

    user.Status = UserStatus.Suspended;
    user.UpdatedAtUtc = DateTime.UtcNow;
    await userManager.UpdateAsync(user);

    await refreshTokenService.RevokeAllForUserAsync(id, ct); // sever active sessions

    var roles = await userManager.GetRolesAsync(user);

    var actorId = Guid.Parse(User.FindFirstValue("sub")!);
    await auditLogger.LogAsync(AuditEventType.UserSuspended, userId: id, actorId: actorId, ct: ct);

    return Ok(user.ToResponse(roles));
  }

  [HttpPost("{id:guid}/reactivate")]
  [Authorize(Roles = "Admin")]
  public async Task<IActionResult> Reactivate(Guid id, CancellationToken ct)
  {
    var user = await userManager.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
    if (user is null)
      return NotFound();

    user.Status = UserStatus.Active;
    user.UpdatedAtUtc = DateTime.UtcNow;
    await userManager.UpdateAsync(user);

    var roles = await userManager.GetRolesAsync(user);

    var actorId = Guid.Parse(User.FindFirstValue("sub")!);
    await auditLogger.LogAsync(AuditEventType.UserReactivated, userId: id, actorId: actorId, ct: ct);

    return Ok(user.ToResponse(roles));
  }

  [HttpDelete("{id:guid}")]
  [Authorize(Roles = "Admin")]
  public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
  {
    if (User.FindFirstValue("sub") == id.ToString())
      return BadRequest(new { message = "You can't delete your own account." });

    var user = await userManager.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
    if (user is null)
      return NotFound();

    user.IsDeleted = true;
    user.UpdatedAtUtc = DateTime.UtcNow;
    await userManager.UpdateAsync(user);

    await refreshTokenService.RevokeAllForUserAsync(id, ct);

    var actorId = Guid.Parse(User.FindFirstValue("sub")!);
    await auditLogger.LogAsync(AuditEventType.UserDeleted, userId: id, actorId: actorId, ct: ct);

    return NoContent();
  }

  [HttpPatch("me")]
  public async Task<IActionResult> UpdateMe(UpdateProfileRequest request)
  {
    var userId = User.FindFirstValue("sub");
    var user = await userManager.FindByIdAsync(userId!);
    if (user is null)
      return Unauthorized();

    if (request.FirstName is not null) user.FirstName = Normalize(request.FirstName);
    if (request.LastName is not null) user.LastName = Normalize(request.LastName);
    if (request.AvatarUrl is not null) user.AvatarUrl = Normalize(request.AvatarUrl);
    if (request.Locale is not null) user.Locale = Normalize(request.Locale);
    if (request.TimeZoneId is not null) user.TimeZoneId = Normalize(request.TimeZoneId);
    if (request.Currency is not null) user.Currency = Normalize(request.Currency)?.ToUpperInvariant();

    user.UpdatedAtUtc = DateTime.UtcNow;
    await userManager.UpdateAsync(user);

    var roles = await userManager.GetRolesAsync(user);

    return Ok(user.ToResponse(roles));
  }

  private static string? Normalize(string value) =>
      string.IsNullOrWhiteSpace(value) ? null : value.Trim();

  [HttpDelete("me")]
  public async Task<IActionResult> DeleteMe(DeleteAccountRequest request, CancellationToken ct)
  {
    var userId = User.FindFirstValue("sub");
    var user = await userManager.FindByIdAsync(userId!);
    if (user is null)
      return Unauthorized();

    // Step-up: re-confirm the password before a destructive, irreversible-feeling action.
    if (!await userManager.CheckPasswordAsync(user, request.Password))
      return BadRequest(new { message = "Password is incorrect." });

    user.IsDeleted = true;
    user.UpdatedAtUtc = DateTime.UtcNow;
    await userManager.UpdateAsync(user);

    await refreshTokenService.RevokeAllForUserAsync(user.Id, ct);

    var actorId = Guid.Parse(User.FindFirstValue("sub")!);
    await auditLogger.LogAsync(AuditEventType.AccountSelfDeleted, user.Id, actorId: user.Id, ct: ct);

    return NoContent();
  }
}