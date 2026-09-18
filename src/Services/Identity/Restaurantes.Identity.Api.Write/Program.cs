using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Restaurantes.Identity.Application;
using Restaurantes.Identity.Infrastructure;
using Restaurantes.Security;
using Restaurantes.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.AddServiceDefaults();
string? keyRingPath = builder.Configuration["ExternalAuth:DataProtectionKeysPath"];
if (!string.IsNullOrWhiteSpace(keyRingPath))
{
    Directory.CreateDirectory(keyRingPath);
    builder
        .Services.AddDataProtection()
        .SetApplicationName("Restaurantes.Identity")
        .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
}
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

if (Configured(builder.Configuration, "Microsoft"))
{
    authentication.AddOpenIdConnect(
        "Microsoft",
        options =>
        {
            options.SignInScheme = "External";
            options.Authority =
                $"https://login.microsoftonline.com/{builder.Configuration["ExternalAuth:Microsoft:TenantId"]}/v2.0";
            options.ClientId = builder.Configuration["ExternalAuth:Microsoft:ClientId"]!;
            options.ClientSecret = builder.Configuration["ExternalAuth:Microsoft:ClientSecret"]!;
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.UsePkce = true;
            options.MapInboundClaims = false;
            options.SaveTokens = false;
            options.CallbackPath = "/api/identity/auth/callback/microsoft";
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");
            SetPublicRedirect(options, builder.Configuration, "microsoft");
        }
    );
}

if (Configured(builder.Configuration, "Google"))
{
    authentication.AddOpenIdConnect(
        "Google",
        options =>
        {
            options.SignInScheme = "External";
            options.Authority = "https://accounts.google.com";
            options.ClientId = builder.Configuration["ExternalAuth:Google:ClientId"]!;
            options.ClientSecret = builder.Configuration["ExternalAuth:Google:ClientSecret"]!;
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.UsePkce = true;
            options.MapInboundClaims = false;
            options.SaveTokens = false;
            options.CallbackPath = "/api/identity/auth/callback/google";
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");
            SetPublicRedirect(options, builder.Configuration, "google");
        }
    );
}

builder.Services.AddControllers();

WebApplication app = builder.Build();
await app.Services.InitializeIdentityDatabaseAsync(app.Configuration);
app.MapServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

static bool Configured(IConfiguration configuration, string provider)
{
    return !string.IsNullOrWhiteSpace(configuration["ExternalAuth:PublicOrigin"])
        && (
            provider != "Microsoft"
            || !string.IsNullOrWhiteSpace(configuration["ExternalAuth:Microsoft:TenantId"])
        )
        && (
            provider != "Google"
            || !string.IsNullOrWhiteSpace(configuration["ExternalAuth:Google:WorkspaceDomain"])
        )
        && !string.IsNullOrWhiteSpace(configuration[$"ExternalAuth:{provider}:ClientId"])
        && !string.IsNullOrWhiteSpace(configuration[$"ExternalAuth:{provider}:ClientSecret"]);
}

static void SetPublicRedirect(
    OpenIdConnectOptions options,
    IConfiguration configuration,
    string provider
)
{
    string origin = configuration["ExternalAuth:PublicOrigin"]!.TrimEnd('/');
    string redirect = $"{origin}/api/identity/auth/callback/{provider}";
    options.Events.OnRedirectToIdentityProvider = context =>
    {
        context.ProtocolMessage.RedirectUri = redirect;
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
