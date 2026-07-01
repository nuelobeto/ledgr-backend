namespace Api.Models;

public class Category
{
  public Guid Id { get; set; }
  public string Name { get; set; } = null!;
  public Guid UserId { get; set; }
  public bool IsArchived { get; set; }
  public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
  public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}