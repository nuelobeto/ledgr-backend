using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Api.Data;
using Api.Dtos;
using Api.Models;

namespace Api.Controllers;

[ApiController]
[Route("api/audit")]
[Authorize(Roles = "Admin")]
public class AuditController(AppDbContext db) : ControllerBase
{
  [HttpGet]
  public async Task<IActionResult> GetAll(
    [FromQuery] Guid? userId,
    [FromQuery] Guid? actorId,
    [FromQuery] AuditEventType? eventType,
    [FromQuery] DateTime? from,
    [FromQuery] DateTime? to,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 50,
    CancellationToken ct = default)
  {
    page = Math.Max(page, 1);
    pageSize = Math.Clamp(pageSize, 1, 200);

    var query = db.AuditEvents.AsNoTracking();

    if (userId is not null)
      query = query.Where(e => e.UserId == userId);
    if (actorId is not null)
      query = query.Where(e => e.ActorId == actorId);
    if (eventType is not null)
      query = query.Where(e => e.EventType == eventType);
    if (from is not null)
      query = query.Where(e => e.CreatedAtUtc >= from);
    if (to is not null)
      query = query.Where(e => e.CreatedAtUtc <= to);

    var totalCount = await query.CountAsync(ct);

    var rows = await query
      .OrderByDescending(e => e.CreatedAtUtc)
      .Skip((page - 1) * pageSize)
      .Take(pageSize)
      .ToListAsync(ct);

    var items = rows.Select(e => new AuditEventResponse(
      e.Id, e.CreatedAtUtc, e.EventType.ToString(),
      e.UserId, e.ActorId, e.IpAddress, e.UserAgent, e.Detail)).ToList();

    return Ok(new PagedResponse<AuditEventResponse>(items, page, pageSize, totalCount));
  }
}