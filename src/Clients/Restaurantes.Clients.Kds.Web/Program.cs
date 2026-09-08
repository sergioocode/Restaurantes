WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
WebApplication app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/", () => Results.Redirect("/kds/"));
app.MapGet(
    "/health",
    () => Results.Ok(new { service = "Restaurantes.Clients.Kds.Web", status = "Healthy" })
);

app.Run();
