using System.ComponentModel.DataAnnotations.Schema;

namespace Api.Models;

public class RefreshToken
{
  public Guid Id { get; set; }
  public Guid UserId { get; set; }
  public User User { get; set; } = null!;
  public string TokenHash { get; set; } = null!;
  public Guid FamilyId { get; set; }
  public DateTime ExpiresAtUtc { get; set; }
  public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
  public DateTime? ConsumedAtUtc { get; set; }
  public DateTime? RevokedAtUtc { get; set; }

  [NotMapped]
  public bool IsActive =>
      ConsumedAtUtc is null
      && RevokedAtUtc is null
      && ExpiresAtUtc > DateTime.UtcNow;
}