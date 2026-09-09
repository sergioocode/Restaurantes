using Restaurantes.CashRegister.Application;
using Restaurantes.CashRegister.Infrastructure;
using Restaurantes.Security;
using Restaurantes.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddScoped<CashRegisterService>();
builder.Services.AddCashRegisterInfrastructure(builder.Configuration);
builder.Services.AddRestaurantSecurity(builder.Configuration);
builder.Services.AddControllers();

WebApplication app = builder.Build();
await app.Services.MigrateCashRegisterDatabaseAsync();
app.MapServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
