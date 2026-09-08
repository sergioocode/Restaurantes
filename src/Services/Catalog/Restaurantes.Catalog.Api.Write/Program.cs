using Microsoft.EntityFrameworkCore;
using Restaurantes.Catalog.Application;
using Restaurantes.Catalog.Infrastructure;
using Restaurantes.Catalog.Infrastructure.Persistence.Write;
using Restaurantes.Security;
using Restaurantes.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddControllers();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<CatalogCommandService>();
builder.Services.AddCatalogWriteInfrastructure(builder.Configuration);
builder.Services.AddRestaurantSecurity(builder.Configuration);

WebApplication app = builder.Build();
await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
{
    CatalogWriteDbContext dbContext =
        scope.ServiceProvider.GetRequiredService<CatalogWriteDbContext>();
    await dbContext.Database.MigrateAsync();
}
app.MapServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
