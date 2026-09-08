using Microsoft.EntityFrameworkCore;
using Restaurantes.Catalog.Infrastructure;
using Restaurantes.Catalog.Infrastructure.Persistence.Read;
using Restaurantes.Security;
using Restaurantes.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddControllers();
builder.Services.AddCatalogReadInfrastructure(builder.Configuration);
builder.Services.AddRestaurantSecurity(builder.Configuration);
WebApplication app = builder.Build();
await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
{
    CatalogReadDbContext dbContext =
        scope.ServiceProvider.GetRequiredService<CatalogReadDbContext>();
    await dbContext.Database.MigrateAsync();
}
app.MapServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
