using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Restaurantes.Dining.Api.Write;
using Restaurantes.Dining.Api.Write.Realtime;
using Restaurantes.Dining.Application;
using Restaurantes.Dining.Infrastructure;
using Restaurantes.Security;
using Restaurantes.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
string[] trustedProxyAddresses =
    builder.Configuration.GetSection("QrAccess:TrustedProxyAddresses").Get<string[]>() ?? [];
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();
    foreach (string configuredAddress in trustedProxyAddresses)
    {
        if (!IPAddress.TryParse(configuredAddress, out IPAddress? address))
        {
            throw new InvalidOperationException(
                $"QrAccess:TrustedProxyAddresses contains invalid IP '{configuredAddress}'."
            );
        }

        options.KnownProxies.Add(address);
    }
});
builder.Services.AddScoped<DiningService>();
builder.Services.AddScoped<DiningIntegrationService>();
builder.Services.AddSingleton<IDiningAuthorization, DiningAuthorization>();
builder.Services.AddDiningInfrastructure(builder.Configuration);
builder.Services.AddScoped<IDiningNotifications, DiningNotifications>();
builder.Services.AddRestaurantSecurity(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddSignalR();

WebApplication app = builder.Build();
await app.Services.MigrateDiningDatabaseAsync();

app.MapServiceDefaults();
app.UseForwardedHeaders();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<DiningHub>("/hubs/dining");
app.Run();
