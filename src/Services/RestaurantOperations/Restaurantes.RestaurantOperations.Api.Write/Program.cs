using Microsoft.EntityFrameworkCore;
using Restaurantes.RestaurantOperations.Application;
using Restaurantes.RestaurantOperations.Infrastructure;
using Restaurantes.RestaurantOperations.Infrastructure.Persistence.Write;
using Restaurantes.Security;
using Restaurantes.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddControllers();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<RestaurantCommandService>();
builder.Services.AddRestaurantOperationsWriteInfrastructure(builder.Configuration);
builder.Services.AddRestaurantSecurity(builder.Configuration);

WebApplication app = builder.Build();

await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
{
    RestaurantWriteDbContext dbContext =
        scope.ServiceProvider.GetRequiredService<RestaurantWriteDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.MapServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
