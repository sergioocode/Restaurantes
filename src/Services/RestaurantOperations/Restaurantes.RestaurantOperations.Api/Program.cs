using Microsoft.EntityFrameworkCore;
using Restaurantes.RestaurantOperations.Application;
using Restaurantes.RestaurantOperations.Infrastructure;
using Restaurantes.RestaurantOperations.Infrastructure.Persistence.Read;
using Restaurantes.RestaurantOperations.Infrastructure.Persistence.Write;
using Restaurantes.Security;
using Restaurantes.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddVaultConfiguration();
builder.AddServiceDefaults();
builder.Services.AddControllers();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<RestaurantCommandService>();
builder.Services.AddScoped<CompanyCommandService>();
builder.Services.AddRestaurantOperationsReadInfrastructure(builder.Configuration);
builder.Services.AddRestaurantOperationsWriteInfrastructure(builder.Configuration);
builder.Services.AddRestaurantSecurity(builder.Configuration);

WorkerHost.ConfigureServices(builder.Services, builder.Configuration);
WebApplication app = builder.Build();
await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
{
    await scope
        .ServiceProvider.GetRequiredService<RestaurantWriteDbContext>()
        .Database.MigrateAsync();
    await scope
        .ServiceProvider.GetRequiredService<RestaurantReadDbContext>()
        .Database.MigrateAsync();
}
app.MapServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
await app.RunAsync();
