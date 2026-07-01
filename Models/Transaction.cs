namespace Api.Models;

public enum TransactionType { Income, Expense }

public class Transaction
{
  public Guid Id { get; set; }
  public DateOnly Date { get; set; }
  public TransactionType Type { get; set; }
  public decimal Amount { get; set; }
  public string? Notes { get; set; }
  public Guid CategoryId { get; set; }
  public Category Category { get; set; } = null!;
  public Guid UserId { get; set; }
  public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
  public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}