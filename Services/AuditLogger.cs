using Microsoft.AspNetCore.Http;
using Api.Data;
using Api.Models;

namespace Api.Services;

public interface IAuditLogger
{
  Task LogAsync(AuditEventType eventType, Guid? userId = null,
      Guid? actorId = null, string? detail = null, CancellationToken ct = default);
}

public class AuditLogger(
    AppDbContext db,
    IHttpContextAccessor httpContextAccessor,
    ILogger<AuditLogger> logger) : IAuditLogger
{
  public async Task LogAsync(AuditEventType eventType, Guid? userId = null,
      Guid? actorId = null, string? detail = null, CancellationToken ct = default)
  {
    try
    {
      var http = httpContextAccessor.HttpContext;

      db.AuditEvents.Add(new AuditEvent
      {
        Id = Guid.NewGuid(),
        CreatedAtUtc = DateTime.UtcNow,
        EventType = eventType,
        UserId = userId,
        ActorId = actorId,
        IpAddress = http?.Connection.RemoteIpAddress?.ToString(),
        UserAgent = http?.Request.Headers.UserAgent.ToString(),
        Detail = detail,
      });

      await db.SaveChangesAsync(ct);
    }
    catch (Exception ex)
    {
      // Best-effort: an audit failure must not break the operation it's recording.
      logger.LogError(ex, "Failed to write audit event {EventType}", eventType);
    }
  }
}