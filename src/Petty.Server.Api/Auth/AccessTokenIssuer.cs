using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using Petty.Server.Application.Features.Auth;

namespace Petty.Server.Api.Auth;

public sealed record JwtSettings(string Issuer, string Audience, SymmetricSecurityKey Key);

public sealed class AccessTokenIssuer(JwtSettings settings) : IAccessTokenIssuer
{
    public string Issue(Guid userId, Guid sessionId, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        var token = new JwtSecurityToken(settings.Issuer, settings.Audience,
            [new Claim("sub", userId.ToString()), new Claim("sid", sessionId.ToString()), new Claim("jti", Guid.NewGuid().ToString())],
            now.UtcDateTime, expiresAt.UtcDateTime, new SigningCredentials(settings.Key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
