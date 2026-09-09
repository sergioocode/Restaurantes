using Restaurantes.Identity.Application;
using Restaurantes.Identity.Infrastructure;
using Restaurantes.Security;
using Restaurantes.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddScoped<IdentityService>();
builder.Services.AddIdentityInfrastructure(builder.Configuration);
builder.Services.AddRestaurantSecurity(builder.Configuration);
builder.Services.AddControllers();

WebApplication app = builder.Build();
await app.Services.InitializeIdentityDatabaseAsync(app.Environment.IsDevelopment());
app.MapServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
