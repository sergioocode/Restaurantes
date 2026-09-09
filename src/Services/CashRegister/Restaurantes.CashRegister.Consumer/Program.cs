using Restaurantes.CashRegister.Application;
using Restaurantes.CashRegister.Consumer;
using Restaurantes.CashRegister.Infrastructure;
using Restaurantes.Messaging.RabbitMq;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddCashRegisterInfrastructure(builder.Configuration);
builder.Services.AddScoped<CashPaymentProjectionService>();
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
builder.Services.AddHostedService<CashPaymentProjectionWorker>();
await builder.Build().RunAsync();
