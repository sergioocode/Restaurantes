using Restaurantes.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy
            .WithOrigins("http://localhost:5300", "http://localhost:5600")
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
    () => Results.Ok(new { service = "Restaurantes.Gateway.Public", audience = "public" })
);
app.MapReverseProxy();

app.Run();
