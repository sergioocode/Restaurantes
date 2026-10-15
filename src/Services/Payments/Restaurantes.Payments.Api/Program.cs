using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Payments.Api;
using Restaurantes.Payments.Application;
using Restaurantes.Payments.Infrastructure;
using Restaurantes.Payments.Infrastructure.Persistence.Write;
using Restaurantes.Security;
using Restaurantes.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddVaultConfiguration();
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
builder.Services.AddControllers();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<PaymentCommandService>();
builder.Services.AddPaymentsWriteInfrastructure(builder.Configuration);
builder.Services.AddRestaurantSecurity(builder.Configuration);
WorkerHost.ConfigureServices(builder.Services, builder.Configuration);
WebApplication app = builder.Build();
await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
{
    PaymentWriteDbContext dbContext =
        scope.ServiceProvider.GetRequiredService<PaymentWriteDbContext>();
    await dbContext.Database.MigrateAsync();
}
app.MapServiceDefaults();
app.UseForwardedHeaders();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
await app.RunAsync();
