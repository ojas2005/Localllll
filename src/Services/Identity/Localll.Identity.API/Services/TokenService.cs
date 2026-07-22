using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Localll.Common.Auth;
using Localll.Identity.API.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Localll.Identity.API.Services;

public class TokenService(IOptions<JwtOptions> jwtOptions)
{
    private readonly JwtOptions _options = jwtOptions.Value;

    public (string AccessToken, DateTime ExpiresAtUtc) CreateAccessToken(ApplicationUser user)
    {
        var expires = DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Role, user.Role)
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public RefreshToken CreateRefreshToken(Guid userId) => new()
    {
        UserId = userId,
        Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
        ExpiresAtUtc = DateTime.UtcNow.AddDays(_options.RefreshTokenDays)
    };
}
