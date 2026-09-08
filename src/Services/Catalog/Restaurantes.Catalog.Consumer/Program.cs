using Restaurantes.Catalog.Consumer;
using Restaurantes.Catalog.Infrastructure;
using Restaurantes.Messaging.RabbitMq;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddCatalogReadInfrastructure(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder
    .Services.AddOptions<RabbitMqOptions>()
    .Bind(builder.Configuration.GetSection(RabbitMqOptions.SectionName))
    .Validate(
        x =>
            !string.IsNullOrWhiteSpace(x.HostName)
            && x.Port > 0
            && !string.IsNullOrWhiteSpace(x.UserName)
            && !string.IsNullOrWhiteSpace(x.Password),
        "RabbitMq configuration is invalid."
    )
    .ValidateOnStart();
builder.Services.AddHostedService<CatalogProjectionWorker>();
await builder.Build().RunAsync();
