using Restaurantes.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

WebApplication app = builder.Build();

app.MapServiceDefaults();
app.MapGet(
    "/",
    () => Results.Ok(new { service = "Restaurantes.Gateway.Private", audience = "internal" })
);
app.MapReverseProxy();

app.Run();
