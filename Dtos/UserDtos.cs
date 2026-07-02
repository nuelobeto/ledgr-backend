using System.ComponentModel.DataAnnotations;

using Api.Models;

namespace Api.Dtos;

public record UserResponse(
  Guid Id,
  string Email,
  bool EmailConfirmed,
  bool TwoFactorEnabled,
  string Status,
  IEnumerable<string> Roles,
  string? FirstName,
  string? LastName,
  string? AvatarUrl,
  string? Locale,
  string? TimeZoneId,
  string? Currency,
  DateTime? LastLoginAtUtc,
  DateTime CreatedAtUtc,
  DateTime UpdatedAtUtc);

public static class UserMappingExtensions
{
  public static UserResponse ToResponse(this User user, IEnumerable<string> roles) =>
    new(
      user.Id,
      user.Email!,
      user.EmailConfirmed,
      user.TwoFactorEnabled,
      user.Status.ToString(),
      roles,
      user.FirstName,
      user.LastName,
      user.AvatarUrl,
      user.Locale,
      user.TimeZoneId,
      user.Currency,
      user.LastLoginAtUtc,
      user.CreatedAtUtc,
      user.UpdatedAtUtc);
}

public record DeleteAccountRequest(
    [param: Required]
    string Password
);

public record UpdateProfileRequest(
    [param: StringLength(100)]
    string? FirstName,

    [param: StringLength(100)]
    string? LastName,

    [param: StringLength(2048)]
    [param: Url]
    string? AvatarUrl,

    [param: StringLength(10)]
    string? Locale,

    [param: StringLength(64)]
    string? TimeZoneId,

    // ISO 4217, e.g. "USD" — case doesn't matter here, UserController.UpdateMe uppercases it.
    [param: RegularExpression("^[A-Za-z]{3}$", ErrorMessage = "Currency must be a 3-letter ISO 4217 code.")]
    string? Currency
);