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
    public IssuedAccessToken Issue(ApplicationUser user)
    {
        RestaurantSecurityOptions security = options.Value;
        DateTime now = time.GetUtcNow().UtcDateTime;
        DateTime expires = now.AddMinutes(security.AccessTokenMinutes);
        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new("preferred_username", user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        ];
        if (user.AllRestaurants)
        {
            claims.Add(new Claim(ClaimTypes.Role, user.Role));
        }
        else if (user.RestaurantId is Guid restaurantId)
        {
            claims.Add(new Claim(RestaurantClaimTypes.RestaurantId, restaurantId.ToString()));
            claims.Add(
                new Claim(RestaurantClaimTypes.RestaurantRole, $"{restaurantId:N}:{user.Role}")
            );
            foreach (string permission in RestaurantPermissions.ForRole(user.Role))
            {
                claims.Add(
                    new Claim(
                        RestaurantClaimTypes.Permission,
                        RestaurantSecurityExtensions.ScopedPermission(restaurantId, permission)
                    )
                );
            }
        }

        SigningCredentials credentials = new(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(security.SigningKey)),
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
