using Restaurantes.Messaging.RabbitMq;
using Restaurantes.RestaurantOperations.Consumer;
using Restaurantes.RestaurantOperations.Infrastructure;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddRestaurantOperationsReadInfrastructure(builder.Configuration);
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
builder.Services.AddHostedService<RestaurantProjectionWorker>();

await builder.Build().RunAsync();
