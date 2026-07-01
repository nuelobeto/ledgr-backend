namespace Api.Dtos;

public record AuditEventResponse(
    Guid Id,
    DateTime CreatedAtUtc,
    string EventType,
    Guid? UserId,
    Guid? ActorId,
    string? IpAddress,
    string? UserAgent,
    string? Detail);