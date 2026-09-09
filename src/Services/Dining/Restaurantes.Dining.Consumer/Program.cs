using Restaurantes.Dining.Application;
using Restaurantes.Dining.Consumer;
using Restaurantes.Dining.Infrastructure;
using Restaurantes.Messaging.RabbitMq;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDiningPersistence(builder.Configuration);
builder.Services.AddScoped<DiningIntegrationService>();
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
builder.Services.AddHostedService<DiningIntegrationWorker>();
await builder.Build().RunAsync();
