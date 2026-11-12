using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Restaurantes.Identity.Application;
using Restaurantes.Identity.Infrastructure;
using Restaurantes.Security;
using Restaurantes.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddVaultConfiguration();
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.AddServiceDefaults();

builder.Services.AddScoped<IdentityService>();
builder.Services.AddIdentityInfrastructure(builder.Configuration);
builder.Services.AddRestaurantSecurity(builder.Configuration);

AuthenticationBuilder authentication = builder
    .Services.AddAuthentication()
    .AddCookie(
        "External",
        options =>
        {
            options.Cookie.Name = "RestaurantesExternal";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        }
    );

if (KeycloakConfigured(builder.Configuration))
{
    authentication.AddOpenIdConnect(
        "Keycloak",
        options =>
        {
            options.SignInScheme = "External";
            options.Authority = builder.Configuration["ExternalAuth:Keycloak:Authority"]!;
            options.ClientId = builder.Configuration["ExternalAuth:Keycloak:ClientId"]!;
            options.ClientSecret = builder.Configuration["ExternalAuth:Keycloak:ClientSecret"]!;
            options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.UsePkce = true;
            options.MapInboundClaims = false;
            options.SaveTokens = false;
            options.CallbackPath = "/api/identity/auth/callback/keycloak";
            options.SignedOutCallbackPath = "/api/identity/auth/logout-callback/keycloak";
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");
            SetPublicRedirect(options, builder.Configuration);
        }
    );
}

builder.Services.AddControllers();

WebApplication app = builder.Build();
await app.Services.InitializeIdentityDatabaseAsync();
app.MapServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
await app.RunAsync();

static bool KeycloakConfigured(IConfiguration configuration)
{
    return Uri.TryCreate(
            configuration["ExternalAuth:Keycloak:Authority"],
            UriKind.Absolute,
            out Uri? authority
        )
        && authority.Scheme is "http" or "https"
        && !string.IsNullOrWhiteSpace(configuration["ExternalAuth:PublicOrigin"])
        && !string.IsNullOrWhiteSpace(configuration["ExternalAuth:Keycloak:ClientId"])
        && !string.IsNullOrWhiteSpace(configuration["ExternalAuth:Keycloak:ClientSecret"]);
}

static void SetPublicRedirect(OpenIdConnectOptions options, IConfiguration configuration)
{
    string origin = configuration["ExternalAuth:PublicOrigin"]!.TrimEnd('/');
    string redirect = $"{origin}/api/identity/auth/callback/keycloak";
    string logoutRedirect = $"{origin}/api/identity/auth/logout-callback/keycloak";
    options.Events.OnRedirectToIdentityProvider = context =>
    {
        context.ProtocolMessage.RedirectUri = redirect;
        if (
            context.Properties?.Items.TryGetValue("reauthenticate", out string? reauthenticate)
                == true
            && reauthenticate == "true"
        )
        {
            context.ProtocolMessage.Prompt = "login";
        }
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToIdentityProviderForSignOut = context =>
    {
        context.ProtocolMessage.ClientId = options.ClientId;
        context.ProtocolMessage.PostLogoutRedirectUri = logoutRedirect;
        return Task.CompletedTask;
    };
    options.Events.OnAuthorizationCodeReceived = context =>
    {
        (
            context.TokenEndpointRequest
            ?? throw new InvalidOperationException("OIDC token request is missing.")
        ).RedirectUri = redirect;
        return Task.CompletedTask;
    };
    options.Events.OnRemoteFailure = context =>
    {
        string path =
            context.Properties?.Items.TryGetValue("returnPath", out string? requested) == true
            && requested is "/backoffice/" or "/pos/" or "/commander/" or "/kds/" or "/dashboard/"
                ? requested
                : "/backoffice/";
        context.Response.Redirect($"{path}#login_error=provider");
        context.HandleResponse();
        return Task.CompletedTask;
    };
}
