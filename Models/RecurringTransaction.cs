namespace Api.Models;

public enum Frequency { Daily, Weekly, Monthly, Yearly }

public class RecurringTransaction
{
  public Guid Id { get; set; }

  public TransactionType Type { get; set; }        // Income or Expense — same enum as Transaction
  public decimal Amount { get; set; }              // positive; Type sets direction
  public string? Notes { get; set; }               // "Overleaf", "Train fare to work"
  public Guid CategoryId { get; set; }
  public Category Category { get; set; } = null!;
  public Frequency Frequency { get; set; }
  public int Interval { get; set; } = 1;           // "every N" → fortnightly = Weekly, 2
  public DateOnly NextDueDate { get; set; }        // the engine for "never miss them"
  public DateOnly? EndDate { get; set; }           // null = runs indefinitely
  public bool IsActive { get; set; } = true;       // pause without deleting
  public Guid UserId { get; set; }
  public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
  public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}