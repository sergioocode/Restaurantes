using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Restaurantes.Orders.Application;
using Restaurantes.Orders.Infrastructure;
using Restaurantes.Orders.Infrastructure.Integrations;
using Restaurantes.Orders.Infrastructure.Persistence.Read;
using Restaurantes.Orders.Infrastructure.Persistence.Write;
using Xunit;

namespace Restaurantes.Orders.Domain.Tests;

public sealed class OrdersInfrastructureRegistrationTests
{
    [Fact]
    public void Write_registration_requires_cash_register_address_and_business_timezone()
    {
        ServiceCollection services = new();
        IConfiguration missingRegister = Configuration(
            new Dictionary<string, string?> { ["Business:TimeZoneId"] = "UTC" }
        );

        InvalidOperationException registerError = Assert.Throws<InvalidOperationException>(() =>
            services.AddOrdersWriteInfrastructure(missingRegister)
        );

        Assert.Equal("CashRegister:BaseAddress is required.", registerError.Message);
        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddOrdersWriteInfrastructure(Configuration([]))
        );
    }

    [Fact]
    public void Write_and_read_registrations_add_stores_clients_and_postgres_contexts()
    {
        IConfiguration configuration = Configuration(
            new Dictionary<string, string?>
            {
                ["Business:TimeZoneId"] = "UTC",
                ["CashRegister:BaseAddress"] = "https://cash-register.test/",
                ["Dining:BaseAddress"] = "https://dining.test/",
                ["ConnectionStrings:OrdersWrite"] = "Host=localhost;Database=orders_write",
                ["ConnectionStrings:OrdersRead"] = "Host=localhost;Database=orders_read",
            }
        );
        ServiceCollection services = new();

        services
            .AddOrdersWriteInfrastructure(configuration)
            .AddDiningSessionClient(configuration)
            .AddOrdersReadInfrastructure(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Equal(
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            provider.GetRequiredService<OrderWriteDbContext>().Database.ProviderName
        );
        Assert.Equal(
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            provider.GetRequiredService<OrderReadDbContext>().Database.ProviderName
        );
        Assert.IsType<IOrderWriteStore>(
            provider.GetRequiredService<IOrderWriteStore>(),
            exactMatch: false
        );
        Assert.IsType<IKitchenOrderReadStore>(
            provider.GetRequiredService<IKitchenOrderReadStore>(),
            exactMatch: false
        );
        Assert.IsType<IDiningSessionStore>(
            provider.GetRequiredService<IDiningSessionStore>(),
            exactMatch: false
        );
        Assert.IsType<OrderCashRegister>(provider.GetRequiredService<IOrderCashRegister>());
    }

    [Fact]
    public void Dining_client_registration_requires_its_base_address()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddDiningSessionClient(Configuration([]))
        );

        Assert.Equal("Dining:BaseAddress is required.", error.Message);
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
