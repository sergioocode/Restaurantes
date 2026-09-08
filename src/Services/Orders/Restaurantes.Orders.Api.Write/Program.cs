using Microsoft.EntityFrameworkCore;
using Restaurantes.Orders.Application;
using Restaurantes.Orders.Infrastructure;
using Restaurantes.Orders.Infrastructure.Persistence.Write;
using Restaurantes.Security;
using Restaurantes.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddControllers();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<OrderCommandService>();
builder.Services.AddOrdersWriteInfrastructure(builder.Configuration);
builder.Services.AddDiningSessionClient(builder.Configuration);
builder.Services.AddRestaurantSecurity(builder.Configuration);
builder.Services.AddCashRegisterAvailability(builder.Configuration);

WebApplication app = builder.Build();
await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
{
    OrderWriteDbContext dbContext = scope.ServiceProvider.GetRequiredService<OrderWriteDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.MapServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
