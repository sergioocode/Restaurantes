using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Restaurantes.CashRegister.Application;
using Restaurantes.CashRegister.Infrastructure;
using Restaurantes.CashRegister.Infrastructure.Integrations;
using Restaurantes.CashRegister.Infrastructure.Persistence;
using Restaurantes.CashRegister.Infrastructure.Stores;
using Xunit;

namespace Restaurantes.CashRegister.Api.Tests;

public sealed class CashRegisterInfrastructureRegistrationTests
{
    [Fact]
    public void Registration_requires_both_service_addresses()
    {
        InvalidOperationException ordersError = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddCashRegisterInfrastructure(
                Configuration(new Dictionary<string, string?>())
            )
        );
        Assert.Equal("Orders:BaseAddress is required.", ordersError.Message);

        InvalidOperationException paymentsError = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddCashRegisterInfrastructure(
                Configuration(
                    new Dictionary<string, string?>
                    {
                        ["Orders:BaseAddress"] = "https://orders.test/",
                    }
                )
            )
        );
        Assert.Equal("Payments:BaseAddress is required.", paymentsError.Message);
    }

    [Fact]
    public void Registration_configures_database_clients_store_and_singleton_time_provider()
    {
        IConfiguration configuration = Configuration(
            new Dictionary<string, string?>
            {
                ["Orders:BaseAddress"] = "https://orders.test/",
                ["Payments:BaseAddress"] = "https://payments.test/",
                ["Business:TimeZoneId"] = "UTC",
                ["ConnectionStrings:CashRegisterWrite"] = "Host=localhost;Database=cash_register",
            }
        );
        ServiceCollection services = new();

        services.AddCashRegisterInfrastructure(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Equal(
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            provider.GetRequiredService<CashRegisterDbContext>().Database.ProviderName
        );
        Assert.IsType<CashRegisterStore>(provider.GetRequiredService<ICashRegisterStore>());
        Assert.IsType<OrdersIncompleteOrdersChecker>(
            provider.GetRequiredService<IIncompleteOrdersChecker>()
        );
        HttpClient closePayments = provider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient("CashClosePayments");
        Assert.Equal("https://payments.test/", closePayments.BaseAddress!.ToString());
        Assert.Equal(TimeSpan.FromSeconds(5), closePayments.Timeout);
        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
