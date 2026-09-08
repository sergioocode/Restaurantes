using Restaurantes.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

WebApplication app = builder.Build();
app.MapServiceDefaults();

RouteGroupBuilder integrations = app.MapGroup("/api/integrations");
integrations.MapGet(
    "/",
    () =>
        Results.Ok(
            new
            {
                service = "Integrations",
                capabilities = new[] { "providers", "webhooks", "delivery" },
            }
        )
);

app.Run();
