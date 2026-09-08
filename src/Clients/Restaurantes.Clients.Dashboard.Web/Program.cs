WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
WebApplication app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/", () => Results.Redirect("/login/"));
app.MapGet(
    "/health",
    () => Results.Ok(new { service = "Restaurantes.Clients.Dashboard.Web", status = "Healthy" })
);

app.Run();
