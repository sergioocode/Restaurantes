using Restaurantes.Sales.Api;
using Restaurantes.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddVaultConfiguration();
builder.AddServiceDefaults();
builder.Services.AddControllers();
WorkerHost.ConfigureServices(builder.Services, builder.Configuration);

WebApplication app = builder.Build();

await WorkerHost.MigrateDatabasesAsync(app.Services);

app.MapServiceDefaults();
app.MapControllers();
await app.RunAsync();
