using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Restaurantes.Dining.Api.Write;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Security;
using Restaurantes.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddSingleton(TimeProvider.System);
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
builder.Services.AddDbContext<DiningDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DiningWrite"))
);
builder
    .Services.AddOptions<RabbitMqOptions>()
    .Bind(builder.Configuration.GetSection(RabbitMqOptions.SectionName))
    .Validate(x => x.Port > 0 && !string.IsNullOrWhiteSpace(x.HostName), "RabbitMq invalid")
    .ValidateOnStart();
builder.Services.AddHostedService<DiningIntegrationWorker>();
builder.Services.AddRestaurantSecurity(builder.Configuration);
builder.Services.AddCashRegisterAvailability(builder.Configuration);
builder.Services.AddSignalR();
builder.Services.AddHttpClient(
    "payments",
    client =>
        client.BaseAddress = new Uri(
            builder.Configuration["Payments:BaseAddress"]
                ?? throw new InvalidOperationException("Payments:BaseAddress is required.")
        )
);

WebApplication app = builder.Build();
await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
{
    DiningDbContext db = scope.ServiceProvider.GetRequiredService<DiningDbContext>();
    await db.Database.MigrateAsync();
}

app.MapServiceDefaults();
app.UseForwardedHeaders();
app.UseAuthentication();
app.UseAuthorization();
app.MapDiningEndpoints();
app.MapHub<DiningHub>("/hubs/dining");
app.Run();
