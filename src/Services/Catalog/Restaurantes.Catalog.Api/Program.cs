using Microsoft.EntityFrameworkCore;
using Restaurantes.Catalog.Api.Realtime;
using Restaurantes.Catalog.Api.Services;
using Restaurantes.Catalog.Application;
using Restaurantes.Catalog.Infrastructure;
using Restaurantes.Catalog.Infrastructure.Media;
using Restaurantes.Catalog.Infrastructure.Persistence.Read;
using Restaurantes.Catalog.Infrastructure.Persistence.Write;
using Restaurantes.Security;
using Restaurantes.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddVaultConfiguration();
builder.AddServiceDefaults();
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<CatalogCommandService>();
builder.Services.AddScoped<CatalogMenuSynchronizer>();
builder.Services.AddScoped<CatalogNotifications>();
builder.Services.AddCatalogMedia(builder.Configuration, builder.Environment);
builder.Services.AddCatalogReadInfrastructure(builder.Configuration);
builder.Services.AddCatalogWriteInfrastructure(builder.Configuration);
builder.Services.AddRestaurantSecurity(builder.Configuration);

WorkerHost.ConfigureServices(builder.Services, builder.Configuration);
WebApplication app = builder.Build();
await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<CatalogWriteDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<CatalogReadDbContext>().Database.MigrateAsync();
}
app.MapServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<CatalogHub>("/hubs/catalog");
await app.RunAsync();
