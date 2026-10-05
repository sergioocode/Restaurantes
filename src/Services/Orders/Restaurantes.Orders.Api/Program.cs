using Microsoft.EntityFrameworkCore;
using Restaurantes.Messaging.RabbitMq;
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
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<KdsHub>("/hubs/kds");
await app.RunAsync();
