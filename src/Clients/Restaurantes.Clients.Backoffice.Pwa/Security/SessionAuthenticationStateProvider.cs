using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Restaurantes.Clients.Backoffice.Pwa.Models;

namespace Restaurantes.Clients.Backoffice.Pwa.Security;

public sealed class SessionAuthenticationStateProvider : AuthenticationStateProvider
{
    private AuthenticationState state = new(new ClaimsPrincipal(new ClaimsIdentity()));

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        return Task.FromResult(state);
    }

    public void SetSession(LoginResponse? login)
    {
        if (login is null)
        {
            state = new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }
        else
        {
            List<Claim> claims =
            [
                new(ClaimTypes.NameIdentifier, login.User.Id.ToString()),
                new(ClaimTypes.Name, login.User.DisplayName),
            ];
            foreach (string role in login.AllRestaurantsRoles ?? [])
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            state = new AuthenticationState(
                new ClaimsPrincipal(new ClaimsIdentity(claims, "Restaurantes"))
            );
        }
        NotifyAuthenticationStateChanged(Task.FromResult(state));
    }
}
