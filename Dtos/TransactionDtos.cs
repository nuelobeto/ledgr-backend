using System.ComponentModel.DataAnnotations;

using Api.Models;

namespace Api.Dtos;

public record CreateTransactionRequest(
  [param: Required] DateOnly Date,
  [param: Required] TransactionType Type,
  [param: Required][param: Range(0.01, double.MaxValue)] decimal Amount,
  [param: Required] Guid CategoryId,
  string? Notes
);

public record UpdateTransactionRequest(
  [param: Required] DateOnly Date,
  [param: Required] TransactionType Type,
  [param: Required][param: Range(0.01, double.MaxValue)] decimal Amount,
  [param: Required] Guid CategoryId,
  string? Notes
);

public record CategoryRef(Guid Id, string Name);

public record TransactionResponse(
  Guid Id,
  DateOnly Date,
  TransactionType Type,
  decimal Amount,
  string? Notes,
  CategoryRef Category,
  Guid UserId,
  DateTime CreatedAtUtc,
  DateTime UpdatedAtUtc);

public static class TransactionMappingExtensions
{
  public static TransactionResponse ToResponse(this Transaction transaction) =>
    new(
      transaction.Id,
      transaction.Date,
      transaction.Type,
      transaction.Amount,
      transaction.Notes,
      new CategoryRef(transaction.Category.Id, transaction.Category.Name),
      transaction.UserId,
      transaction.CreatedAtUtc,
      transaction.UpdatedAtUtc);
}