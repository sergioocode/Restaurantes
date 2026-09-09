using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Reporting.Api.Read;
using Restaurantes.Reporting.Application.Dashboard;
using Restaurantes.Reporting.Application.Health;
using Restaurantes.Reporting.Infrastructure;
using Restaurantes.Security;
using Restaurantes.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddScoped<DashboardQueryService>();
builder.Services.AddScoped<ReportingHealthService>();
builder.Services.AddReportingInfrastructure(builder.Configuration);
builder.Services.AddRestaurantSecurity(builder.Configuration);
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
await app.Services.MigrateReportingDatabaseAsync();
app.MapServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<ReportingHub>("/hubs/reporting").RequireAuthorization();
app.Run();
