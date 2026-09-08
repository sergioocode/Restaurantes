using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Orders.Infrastructure;
using Restaurantes.Orders.Publisher;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
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
builder.Services.AddHostedService<OrderOutboxPublisherWorker>();
await builder.Build().RunAsync();
