using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Restaurantes.Identity.Application;
using Restaurantes.Identity.Domain;
using Restaurantes.Security;

namespace Restaurantes.Identity.Infrastructure.Security;

public sealed class AccessTokenService(
    IOptions<RestaurantSecurityOptions> options,
    TimeProvider time
) : IAccessTokenIssuer
{
    public IssuedAccessToken Issue(ApplicationUser user, IReadOnlyCollection<string> globalRoles)
    {
        RestaurantSecurityOptions security = options.Value;
        DateTime now = time.GetUtcNow().UtcDateTime;
        DateTime expires = now.AddMinutes(security.AccessTokenMinutes);
        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new("preferred_username", user.UserName ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("security_stamp", user.SecurityStamp ?? string.Empty),
        ];

        foreach (string role in globalRoles.Distinct(StringComparer.Ordinal))
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        foreach (
            UserRestaurantAssignment access in user.RestaurantAccesses.Where(x =>
                x.IsCurrentlyActive(now)
            )
        )
        {
            claims.Add(
                new Claim(RestaurantClaimTypes.RestaurantId, access.RestaurantId.ToString())
            );
            claims.Add(
                new Claim(
                    RestaurantClaimTypes.RestaurantRole,
                    $"{access.RestaurantId:N}:{access.Role}"
                )
            );
            foreach (string permission in RestaurantPermissions.ForRole(access.Role))
            {
                claims.Add(
                    new Claim(
                        RestaurantClaimTypes.Permission,
                        RestaurantSecurityExtensions.ScopedPermission(
                            access.RestaurantId,
                            permission
                        )
                    )
                );
            }
        }

        SigningCredentials credentials = new(
            new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(security.SigningKey)),
            SecurityAlgorithms.HmacSha256
        );
        JwtSecurityToken token = new(
            security.Issuer,
            security.Audience,
            claims,
            now,
            expires,
            credentials
        );
        return new IssuedAccessToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
