using Restaurantes.Messaging.RabbitMq;
using Restaurantes.Sales.Application;
using Restaurantes.Sales.Consumer;
using Restaurantes.Sales.Infrastructure;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSalesWriteInfrastructure(builder.Configuration);
builder.Services.AddSalesReadInfrastructure(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<SaleCommandService>();
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
builder.Services.AddHostedService<CheckoutProcessWorker>();
builder.Services.AddHostedService<SalesReadProjectionWorker>();
await builder.Build().RunAsync();
