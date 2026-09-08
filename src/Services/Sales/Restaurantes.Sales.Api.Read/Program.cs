using Microsoft.EntityFrameworkCore;
using Restaurantes.Sales.Infrastructure;
using Restaurantes.Sales.Infrastructure.Persistence.Read;
using Restaurantes.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddControllers();
builder.Services.AddSalesReadInfrastructure(builder.Configuration);

WebApplication app = builder.Build();

await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
{
    SaleReadDbContext dbContext = scope.ServiceProvider.GetRequiredService<SaleReadDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.MapServiceDefaults();
app.MapControllers();
app.Run();
