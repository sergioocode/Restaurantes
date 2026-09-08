using Microsoft.EntityFrameworkCore;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Orders.Api.Read.Realtime;
using Restaurantes.Orders.Infrastructure;
using Restaurantes.Orders.Infrastructure.Persistence.Read;
using Restaurantes.Security;
using Restaurantes.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddOrdersReadInfrastructure(builder.Configuration);
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

WebApplication app = builder.Build();
await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
{
    OrderReadDbContext dbContext = scope.ServiceProvider.GetRequiredService<OrderReadDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.MapServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<KdsHub>("/hubs/kds");
app.Run();
