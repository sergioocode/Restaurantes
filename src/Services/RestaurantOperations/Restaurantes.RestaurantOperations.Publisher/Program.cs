using Restaurantes.Messaging.RabbitMq;
using Restaurantes.RestaurantOperations.Infrastructure;
using Restaurantes.RestaurantOperations.Publisher;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddRestaurantOperationsWriteInfrastructure(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder
    .Services.AddOptions<RabbitMqOptions>()
    .Bind(builder.Configuration.GetSection(RabbitMqOptions.SectionName))
    .Validate(
        options =>
            !string.IsNullOrWhiteSpace(options.HostName)
            && options.Port is > 0 and <= 65_535
            && !string.IsNullOrWhiteSpace(options.UserName)
            && !string.IsNullOrWhiteSpace(options.Password)
            && !string.IsNullOrWhiteSpace(options.VirtualHost),
        "RabbitMq configuration is invalid."
    )
    .ValidateOnStart();
builder.Services.AddHostedService<OutboxPublisherWorker>();

await builder.Build().RunAsync();
