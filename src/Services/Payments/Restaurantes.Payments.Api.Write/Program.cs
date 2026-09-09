using Microsoft.EntityFrameworkCore;
using Restaurantes.Payments.Application;
using Restaurantes.Payments.Infrastructure;
using Restaurantes.Payments.Infrastructure.Persistence.Write;
using Restaurantes.Security;
using Restaurantes.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddControllers();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<PaymentCommandService>();
builder.Services.AddPaymentsWriteInfrastructure(builder.Configuration);
builder.Services.AddRestaurantSecurity(builder.Configuration);
WebApplication app = builder.Build();
await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
{
    PaymentWriteDbContext dbContext =
        scope.ServiceProvider.GetRequiredService<PaymentWriteDbContext>();
    await dbContext.Database.MigrateAsync();
}
app.MapServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
