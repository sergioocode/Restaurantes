using Microsoft.EntityFrameworkCore;
using Restaurantes.RestaurantOperations.Infrastructure;
using Restaurantes.RestaurantOperations.Infrastructure.Persistence.Read;
using Restaurantes.Security;
using Restaurantes.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddControllers();
builder.Services.AddRestaurantOperationsReadInfrastructure(builder.Configuration);
builder.Services.AddRestaurantSecurity(builder.Configuration);

WebApplication app = builder.Build();

await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
{
    RestaurantReadDbContext dbContext =
        scope.ServiceProvider.GetRequiredService<RestaurantReadDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.MapServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
