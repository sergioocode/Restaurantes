using Restaurantes.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy
            .WithOrigins(
                "http://localhost:5000",
                "http://localhost:5700",
                "http://localhost:5200",
                "http://localhost:5400",
                "http://localhost:5500",
                "http://localhost:5600"
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
    )
);
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

WebApplication app = builder.Build();

app.UseCors();
app.MapServiceDefaults();
app.MapGet(
    "/",
    () => Results.Ok(new { service = "Restaurantes.Gateway.Private", audience = "internal" })
);
app.MapReverseProxy();

app.Run();
