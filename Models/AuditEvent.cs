namespace Api.Models;

public enum AuditEventType
{
  LoginSucceeded, LoginFailed, LoginLockedOut,
  MfaChallengeIssued, MfaSucceeded, MfaFailed,
  EmailConfirmed,
  PasswordChanged, PasswordResetRequested, PasswordReset,
  MfaEnabled, MfaDisabled, RecoveryCodesRegenerated,
  UserSuspended, UserReactivated, UserDeleted, AccountSelfDeleted,
  SessionsRevoked
}

public class AuditEvent
{
  public Guid Id { get; set; }
  public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
  public AuditEventType EventType { get; set; }
  public Guid? UserId { get; set; }    // the account the event concerns
  public Guid? ActorId { get; set; }   // who performed it (admin actions); null = self/system
  public string? IpAddress { get; set; }
  public string? UserAgent { get; set; }
  public string? Detail { get; set; }
}