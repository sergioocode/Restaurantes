using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Payments.Infrastructure;
using Restaurantes.Payments.Publisher;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddPaymentsWriteInfrastructure(builder.Configuration);
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
builder.Services.AddHostedService<PaymentOutboxPublisherWorker>();
await builder.Build().RunAsync();
