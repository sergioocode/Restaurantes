using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Restaurantes.Payments.Application;
using Restaurantes.Payments.Infrastructure;
using Restaurantes.Payments.Infrastructure.Integrations;
using Restaurantes.Payments.Infrastructure.Persistence.Write;
using Restaurantes.Payments.Infrastructure.Queries;
using Restaurantes.Payments.Infrastructure.Stores;
using Xunit;

namespace Restaurantes.Payments.Api.Tests;

public sealed class PaymentsInfrastructureRegistrationTests
{
    [Fact]
    public void Registration_requires_dining_and_cash_register_addresses()
    {
        InvalidOperationException diningError = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddPaymentsWriteInfrastructure(
                Configuration(new Dictionary<string, string?>())
            )
        );
        Assert.Equal("Dining:BaseAddress is required.", diningError.Message);

        InvalidOperationException registerError = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddPaymentsWriteInfrastructure(
                Configuration(
                    new Dictionary<string, string?>
                    {
                        ["Dining:BaseAddress"] = "https://dining.test/",
                    }
                )
            )
        );
        Assert.Equal("CashRegister:BaseAddress is required.", registerError.Message);
    }

    [Fact]
    public void Registration_adds_payment_store_queries_validators_and_postgres_context()
    {
        IConfiguration configuration = Configuration(
            new Dictionary<string, string?>
            {
                ["Dining:BaseAddress"] = "https://dining.test/",
                ["CashRegister:BaseAddress"] = "https://cash-register.test/",
                ["ConnectionStrings:PaymentsWrite"] = "Host=localhost;Database=payments",
            }
        );
        ServiceCollection services = new();

        services.AddPaymentsWriteInfrastructure(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Equal(
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            provider.GetRequiredService<PaymentWriteDbContext>().Database.ProviderName
        );
        Assert.IsType<PaymentWriteStore>(provider.GetRequiredService<IPaymentWriteStore>());
        Assert.IsType<PaymentStatusQuery>(provider.GetRequiredService<IPaymentStatusQuery>());
        Assert.IsType<DiningSessionValidator>(
            provider.GetRequiredService<ICustomerDiningSessionValidator>()
        );
        Assert.IsType<PaymentCashRegister>(provider.GetRequiredService<IPaymentCashRegister>());
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
