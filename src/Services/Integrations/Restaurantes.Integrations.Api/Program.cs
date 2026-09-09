using Restaurantes.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddControllers();

WebApplication app = builder.Build();
app.MapServiceDefaults();
app.MapControllers();

app.Run();
