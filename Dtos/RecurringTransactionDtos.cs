using System.ComponentModel.DataAnnotations;

using Api.Models;

namespace Api.Dtos;

public record CreateRecurringTransactionRequest(
    [param: Required] TransactionType Type,
    [param: Required][param: Range(0.01, double.MaxValue)] decimal Amount,
    [param: Required] Guid CategoryId,
    [param: Required] Frequency Frequency,
    [param: Range(1, int.MaxValue)] int Interval,
    [param: Required] DateOnly NextDueDate,
    DateOnly? EndDate,
    string? Notes
);

public record UpdateRecurringTransactionRequest(
    [param: Required] TransactionType Type,
    [param: Required][param: Range(0.01, double.MaxValue)] decimal Amount,
    [param: Required] Guid CategoryId,
    [param: Required] Frequency Frequency,
    [param: Range(1, int.MaxValue)] int Interval,
    [param: Required] DateOnly NextDueDate,
    DateOnly? EndDate,
    bool IsActive,
    string? Notes
);

public record RecurringTransactionResponse(
  Guid Id,
  TransactionType Type,
  decimal Amount,
  string? Notes,
  CategoryRef Category,
  Frequency Frequency,
  int Interval,
  DateOnly NextDueDate,
  DateOnly? EndDate,
  bool IsActive,
  Guid UserId,
  DateTime CreatedAtUtc,
  DateTime UpdatedAtUtc);

public static class RecurringTransactionMappingExtensions
{
  public static RecurringTransactionResponse ToResponse(this RecurringTransaction recurring) =>
    new(
      recurring.Id,
      recurring.Type,
      recurring.Amount,
      recurring.Notes,
      new CategoryRef(recurring.Category.Id, recurring.Category.Name),
      recurring.Frequency,
      recurring.Interval,
      recurring.NextDueDate,
      recurring.EndDate,
      recurring.IsActive,
      recurring.UserId,
      recurring.CreatedAtUtc,
      recurring.UpdatedAtUtc);
}