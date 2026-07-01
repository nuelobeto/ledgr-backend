using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Api.Data;
using Api.Models;

namespace Api.Services;

public interface IRefreshTokenService
{
  Task<string> IssueForNewSessionAsync(User user, CancellationToken ct = default);
  Task<RefreshResult> ValidateAndRotateAsync(string rawToken, CancellationToken ct = default);
  Task RevokeByTokenAsync(string rawToken, CancellationToken ct = default);
  Task RevokeFamilyAsync(Guid familyId, CancellationToken ct = default);
  Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default);
}

public record RefreshResult(bool Succeeded, User? User, string? RefreshToken)
{
  public static RefreshResult Fail() => new(false, null, null);
  public static RefreshResult Success(User user, string refreshToken)
      => new(true, user, refreshToken);
}

public class RefreshTokenService(AppDbContext db, IConfiguration configuration)
    : IRefreshTokenService
{
  private readonly int _refreshTokenDays =
      int.TryParse(configuration["JWT_REFRESH_TOKEN_DAYS"], out var days) ? days : 14;

  public async Task<string> IssueForNewSessionAsync(User user, CancellationToken ct = default)
  {
    var (raw, entity) = CreateToken(user.Id, Guid.NewGuid()); // new family
    db.RefreshTokens.Add(entity);
    await db.SaveChangesAsync(ct);
    return raw;
  }

  public async Task<RefreshResult> ValidateAndRotateAsync(string rawToken, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(rawToken))
      return RefreshResult.Fail();

    var hash = Hash(rawToken);
    var stored = await db.RefreshTokens
        .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

    if (stored is null)
      return RefreshResult.Fail(); // unknown / forged

    // Already consumed or revoked → a replay. Treat as theft: kill the family.
    if (stored.ConsumedAtUtc is not null || stored.RevokedAtUtc is not null)
    {
      await RevokeFamilyAsync(stored.FamilyId, ct);
      return RefreshResult.Fail();
    }

    if (stored.ExpiresAtUtc <= DateTime.UtcNow)
      return RefreshResult.Fail();

    // Is the account still allowed a session? Load past the soft-delete filter.
    var user = await db.Users
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(u => u.Id == stored.UserId, ct);

    if (user is null || user.IsDeleted || user.Status == UserStatus.Suspended)
    {
      await RevokeFamilyAsync(stored.FamilyId, ct);
      return RefreshResult.Fail();
    }

    // Rotate: consume this link, mint its successor in the same family.
    stored.ConsumedAtUtc = DateTime.UtcNow;
    var (raw, successor) = CreateToken(user.Id, stored.FamilyId);
    db.RefreshTokens.Add(successor);
    await db.SaveChangesAsync(ct);

    return RefreshResult.Success(user, raw);
  }

  public Task RevokeByTokenAsync(string rawToken, CancellationToken ct = default)
      => RevokeFamilyByHashAsync(Hash(rawToken), ct);

  public async Task RevokeFamilyAsync(Guid familyId, CancellationToken ct = default)
  {
    await db.RefreshTokens
        .Where(t => t.FamilyId == familyId && t.RevokedAtUtc == null)
        .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, DateTime.UtcNow), ct);
  }

  public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
  {
    await db.RefreshTokens
        .Where(t => t.UserId == userId && t.RevokedAtUtc == null)
        .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, DateTime.UtcNow), ct);
  }

  private async Task RevokeFamilyByHashAsync(string hash, CancellationToken ct)
  {
    var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
    if (stored is not null)
      await RevokeFamilyAsync(stored.FamilyId, ct);
  }

  private (string rawToken, RefreshToken entity) CreateToken(Guid userId, Guid familyId)
  {
    var raw = GenerateRawToken();
    var entity = new RefreshToken
    {
      Id = Guid.NewGuid(),
      UserId = userId,
      FamilyId = familyId,
      TokenHash = Hash(raw),
      CreatedAtUtc = DateTime.UtcNow,
      ExpiresAtUtc = DateTime.UtcNow.AddDays(_refreshTokenDays),
    };
    return (raw, entity);
  }

  private static string GenerateRawToken()
      => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); // 256-bit opaque token

  private static string Hash(string rawToken)
      => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}