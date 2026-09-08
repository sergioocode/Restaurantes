using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Sales.Infrastructure;
using Restaurantes.Sales.Publisher;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSalesWriteInfrastructure(builder.Configuration);
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
builder.Services.AddHostedService<SalesOutboxPublisherWorker>();
await builder.Build().RunAsync();
