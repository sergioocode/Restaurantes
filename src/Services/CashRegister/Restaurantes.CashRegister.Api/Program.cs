using Microsoft.EntityFrameworkCore;
using Restaurantes.CashRegister.Api;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Security;
using Restaurantes.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<CashRegisterDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("CashRegisterWrite"))
);
builder.Services.AddRestaurantSecurity(builder.Configuration);
builder
    .Services.AddOptions<RabbitMqOptions>()
    .Bind(builder.Configuration.GetSection(RabbitMqOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHostedService<CashPaymentProjectionWorker>();

WebApplication app = builder.Build();
await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
{
    CashRegisterDbContext dbContext =
        scope.ServiceProvider.GetRequiredService<CashRegisterDbContext>();
    await dbContext.Database.MigrateAsync();
}
app.MapServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapCashRegisterEndpoints();
app.Run();
