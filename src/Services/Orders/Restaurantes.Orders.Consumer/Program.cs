using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Orders.Consumer;
using Restaurantes.Orders.Infrastructure;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddOrdersReadInfrastructure(builder.Configuration);
builder.Services.AddOrdersWriteInfrastructure(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder
    .Services.AddOptions<RabbitMqOptions>()
    .Bind(builder.Configuration.GetSection(RabbitMqOptions.SectionName))
    .Validate(
        options =>
            !string.IsNullOrWhiteSpace(options.HostName)
            && options.Port > 0
            && !string.IsNullOrWhiteSpace(options.UserName)
            && !string.IsNullOrWhiteSpace(options.Password),
        "RabbitMq configuration is invalid."
    )
    .ValidateOnStart();
builder.Services.AddHostedService<KdsProjectionWorker>();
builder.Services.AddHostedService<CatalogProjectionWorker>();
builder.Services.AddHostedService<PaymentProjectionWorker>();
await builder.Build().RunAsync();
