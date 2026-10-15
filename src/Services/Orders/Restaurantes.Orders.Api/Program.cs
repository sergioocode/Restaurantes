using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Orders.Api;
using Restaurantes.Orders.Api.Realtime;
using Restaurantes.Orders.Application;
using Restaurantes.Orders.Infrastructure;
using Restaurantes.Orders.Infrastructure.Persistence.Read;
using Restaurantes.Orders.Infrastructure.Persistence.Write;
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
builder.Services.AddSignalR();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<OrderCommandService>();
builder.Services.AddOrdersReadInfrastructure(builder.Configuration);
builder.Services.AddOrdersWriteInfrastructure(builder.Configuration);
builder.Services.AddDiningSessionClient(builder.Configuration);
builder.Services.AddRestaurantSecurity(builder.Configuration);
builder
    .Services.AddOptions<RabbitMqOptions>()
    .Bind(builder.Configuration.GetSection(RabbitMqOptions.SectionName))
    .Validate(
        options =>
            !string.IsNullOrWhiteSpace(options.HostName)
            && options.Port > 0
            && !string.IsNullOrWhiteSpace(options.UserName)
            && !string.IsNullOrWhiteSpace(options.Password),
        "RabbitMq configuration is invalid."
    )
    .ValidateOnStart();
builder.Services.AddHostedService<KdsSignalRWorker>();

WorkerHost.ConfigureServices(builder.Services, builder.Configuration);
WebApplication app = builder.Build();
await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<OrderWriteDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<OrderReadDbContext>().Database.MigrateAsync();
}
app.MapServiceDefaults();
app.UseForwardedHeaders();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<KdsHub>("/hubs/kds");
app.MapHub<OrdersHub>("/hubs/orders");
await app.RunAsync();
