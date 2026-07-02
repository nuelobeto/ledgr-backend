using Microsoft.AspNetCore.Identity;

namespace Api.Models;

public enum UserStatus { Active, Suspended }

public class User : IdentityUser<Guid>
{
  public UserStatus Status { get; set; } = UserStatus.Active;
  public bool IsDeleted { get; set; }
  public string? FirstName { get; set; }
  public string? LastName { get; set; }
  public string? AvatarUrl { get; set; }
  public string? Locale { get; set; }
  public string? TimeZoneId { get; set; }
  public string? Currency { get; set; } // ISO 4217 code, e.g. "USD" — null until setup completes
  public DateTime? LastLoginAtUtc { get; set; }
  public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
  public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}