using Microsoft.EntityFrameworkCore;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Reporting.Api.Read;
using Restaurantes.Reporting.Infrastructure.Persistence.Read;
using Restaurantes.Reporting.Infrastructure.Queries;
using Restaurantes.Security;
using Restaurantes.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddHttpClient();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddRestaurantSecurity(builder.Configuration);
builder.Services.AddDbContext<ReportingReadDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("ReportingRead"))
);
builder.Services.AddScoped<ReportingQueries>();
builder
    .Services.AddOptions<RabbitMqOptions>()
    .Bind(builder.Configuration.GetSection(RabbitMqOptions.SectionName))
    .Validate(
        x =>
            x.Port > 0
            && !string.IsNullOrWhiteSpace(x.HostName)
            && !string.IsNullOrWhiteSpace(x.UserName)
            && !string.IsNullOrWhiteSpace(x.Password),
        "RabbitMq invalid"
    )
    .ValidateOnStart();
builder.Services.AddHostedService<ReportingSignalRWorker>();
WebApplication app = builder.Build();
await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
{
    ReportingReadDbContext dbContext =
        scope.ServiceProvider.GetRequiredService<ReportingReadDbContext>();
    await dbContext.Database.MigrateAsync();
}
app.MapServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<ReportingHub>("/hubs/reporting").RequireAuthorization();
app.Run();
