using Microsoft.EntityFrameworkCore;
using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Reporting.Consumer;
using Restaurantes.Reporting.Consumer.Handlers;
using Restaurantes.Reporting.Infrastructure.Persistence.Read;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDbContext<ReportingReadDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("ReportingRead"))
);
builder.Services.AddSingleton(TimeProvider.System);
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
builder.Services.AddScoped<ReportingProjectionHandler>();
builder.Services.AddHostedService<ReportingProjectionWorker>();
await builder.Build().RunAsync();
