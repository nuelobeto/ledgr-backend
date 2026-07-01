using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Api.Models;

namespace Api.Services;

public interface ITokenService
{
  string CreateAccessToken(User user, IEnumerable<string> roles);
  string CreateMfaChallengeToken(User user);
  string? ValidateMfaChallengeToken(string token);
  string CreateEnrollmentToken(User user);
}

public class TokenService(IConfiguration config) : ITokenService
{
  public const string MfaChallengeAudience = "mfa-challenge";
  public const string EnrollmentAudience = "mfa-enrollment";

  public string CreateAccessToken(User user, IEnumerable<string> roles)
  {
    var claims = new List<Claim>
    {
      new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
      new(JwtRegisteredClaimNames.Email, user.Email!),
      new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
      new("security_stamp", user.SecurityStamp!)
    };
    claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["JWT_SIGNING_KEY"]!));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var minutes = int.Parse(config["JWT_ACCESS_TOKEN_MINUTES"] ?? "15");

    var token = new JwtSecurityToken(
      issuer: config["JWT_ISSUER"],
      audience: config["JWT_AUDIENCE"],
      claims: claims,
      expires: DateTime.UtcNow.AddMinutes(minutes),
      signingCredentials: creds);

    return new JwtSecurityTokenHandler().WriteToken(token);
  }

  public string CreateMfaChallengeToken(User user)
      => CreatePurposeToken(user, MfaChallengeAudience, "mfa", TimeSpan.FromMinutes(5));

  public string CreateEnrollmentToken(User user)
      => CreatePurposeToken(user, EnrollmentAudience, "mfa-enrollment", TimeSpan.FromMinutes(15));

  public string? ValidateMfaChallengeToken(string token)
  {
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["JWT_SIGNING_KEY"]!));
    var parameters = new TokenValidationParameters
    {
      ValidateIssuer = true,
      ValidIssuer = config["JWT_ISSUER"],
      ValidateAudience = true,
      ValidAudience = MfaChallengeAudience,
      ValidateIssuerSigningKey = true,
      IssuerSigningKey = key,
      ValidateLifetime = true,
      ClockSkew = TimeSpan.FromSeconds(30),
    };

    try
    {
      var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
      var principal = handler.ValidateToken(token, parameters, out _);
      return principal.FindFirstValue("sub");
    }
    catch
    {
      return null; // expired, tampered, or wrong audience
    }
  }

  private string CreatePurposeToken(User user, string audience, string purpose, TimeSpan lifetime)
  {
    var claims = new[]
    {
      new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
      new Claim("purpose", purpose),
      new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
    };

    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["JWT_SIGNING_KEY"]!));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

    var token = new JwtSecurityToken(
      issuer: config["JWT_ISSUER"],
      audience: audience,
      claims: claims,
      expires: DateTime.UtcNow.Add(lifetime),
      signingCredentials: creds);

    return new JwtSecurityTokenHandler().WriteToken(token);
  }
}